<?php
declare(strict_types=1);

final class BoundedRepairException extends RuntimeException {}

/** Fail-closed, file-oriented repair boundary for protected broker/PWA artifacts. */
final class BoundedRepairService
{
    private const APPROVAL = 'REPAIR_APPROVED';
    private const MAX_OPERATIONS = 16;
    private const MAX_BYTES = 1048576;
    private const RESOURCES = ['xp-awards','awards','word-counts','quests','messages','roster','broker-state','cache','deployment'];

    public function __construct(
        private readonly string $root,
        private readonly string $journalPath,
        private readonly string $generation,
        private readonly array $resourceOwners = [],
    ) {}

    public function execute(array $request, bool $dryRun = true, ?callable $cancelled = null): array
    {
        $plan = $this->validate($request);
        $journal = ['repair_id'=>$plan['repair_id'],'operator_id'=>$plan['operator_id'],'account_id'=>$plan['account_id'],'generation'=>$plan['generation'],'mode'=>$dryRun?'dry-run':'repair','operations'=>[]];
        foreach ($plan['operations'] as $operation) {
            $path = $operation['path'];
            $before = hash_file('sha256', $path);
            $journal['operations'][] = ['resource'=>$operation['resource'],'record_id'=>$operation['record_id'],'path'=>basename($path),'before_sha256'=>$before,'after_sha256'=>$operation['after_sha256']];
        }
        $journal['request_sha256'] = hash('sha256', json_encode($request, JSON_THROW_ON_ERROR));
        if ($dryRun) { $this->appendJournal($journal); return ['status'=>'dry-run','repair_id'=>$plan['repair_id'],'operations'=>$journal['operations']]; }
        if ($this->isCancelled($cancelled)) throw new BoundedRepairException('repair_cancelled');
        $originals = [];
        try {
            foreach ($plan['operations'] as $index=>$operation) {
                if ($this->isCancelled($cancelled)) throw new BoundedRepairException('repair_cancelled');
                $path=$operation['path']; $originals[$path]=file_get_contents($path);
                if ($originals[$path] === false || !hash_equals(hash('sha256',$originals[$path]), $operation['before_sha256'])) throw new BoundedRepairException('stale_record');
                $bytes=base64_decode($operation['content_base64'], true);
                if ($bytes === false) throw new BoundedRepairException('malformed_content');
                $tmp=$path.'.repair-'.bin2hex(random_bytes(6));
                if (file_put_contents($tmp,$bytes,LOCK_EX) === false || !hash_equals(hash_file('sha256',$tmp),$operation['after_sha256'])) { @unlink($tmp); throw new BoundedRepairException('after_hash_mismatch'); }
                if (!rename($tmp,$path)) { @unlink($tmp); throw new BoundedRepairException('promotion_failed'); }
                $journal['operations'][$index]['applied']=true;
            }
            $journal['status']='applied'; $this->appendJournal($journal);
            return ['status'=>'applied','repair_id'=>$plan['repair_id'],'operations'=>$journal['operations']];
        } catch (Throwable $error) {
            foreach ($originals as $path=>$bytes) { if (is_string($bytes)) { $tmp=$path.'.rollback-'.bin2hex(random_bytes(6)); if (file_put_contents($tmp,$bytes,LOCK_EX)!==false) @rename($tmp,$path); } }
            $journal['status']='rolled_back'; $journal['error_code']=$error instanceof BoundedRepairException ? $error->getMessage() : 'repair_failed';
            try { $this->appendJournal($journal); } catch (Throwable) {}
            throw $error;
        }
    }

    private function validate(array $request): array
    {
        if (($request['approval'] ?? null)!==self::APPROVAL || ($request['mode'] ?? null)!=='repair') throw new BoundedRepairException('operator_approval_required');
        foreach (['repair_id','operator_id','account_id','generation','expected_generation'] as $key) if (!is_string($request[$key]??null) || trim($request[$key])==='') throw new BoundedRepairException('malformed_request');
        if (!hash_equals($this->generation,$request['expected_generation']) || !hash_equals($this->generation,$request['generation'])) throw new BoundedRepairException('stale_generation');
        if (preg_match('/^[a-zA-Z0-9._:-]{1,80}$/D',$request['repair_id'])!==1 || preg_match('/^[a-zA-Z0-9._:-]{1,80}$/D',$request['operator_id'])!==1 || preg_match('/^[a-f0-9]{32}$/D',$request['account_id'])!==1) throw new BoundedRepairException('malformed_identity');
        $ops=$request['operations']??null; if (!is_array($ops) || $ops===[] || count($ops)>self::MAX_OPERATIONS) throw new BoundedRepairException('operation_limit');
        $normalized=[]; $seen=[];
        foreach ($ops as $op) {
            if (!is_array($op) || !in_array($op['resource']??'',self::RESOURCES,true) || !is_string($op['record_id']??null) || !is_string($op['owner_account_id']??null) || !hash_equals($request['account_id'],$op['owner_account_id']) || !is_string($op['path']??null) || !is_string($op['before_sha256']??null) || !is_string($op['after_sha256']??null) || !is_string($op['content_base64']??null) || ($op['generation']??null)!==$this->generation) throw new BoundedRepairException('ownership_or_shape_rejected');
            if (($this->resourceOwners[$op['resource']]??null)!==$op['owner_account_id']) throw new BoundedRepairException('ownership_ambiguous');
            if (preg_match('/^[a-zA-Z0-9._:-]{1,120}$/D',$op['record_id'])!==1 || preg_match('/^[a-f0-9]{64}$/D',$op['before_sha256'])!==1 || preg_match('/^[a-f0-9]{64}$/D',$op['after_sha256'])!==1) throw new BoundedRepairException('malformed_request');
            $root=realpath($this->root); $candidate=realpath(dirname($op['path']));
            if ($root===false || $candidate===false || !hash_equals($root,$candidate) || basename($op['path'])!==$op['record_id'].'.json' || !is_file($op['path']) || is_link($op['path'])) throw new BoundedRepairException('unsafe_path');
            $bytes=base64_decode($op['content_base64'],true); if ($bytes===false || strlen($bytes)>self::MAX_BYTES) throw new BoundedRepairException('repair_size_limit');
            if (isset($seen[$op['path']])) throw new BoundedRepairException('duplicate_target'); $seen[$op['path']]=true; $normalized[]=$op;
        }
        return ['repair_id'=>$request['repair_id'],'operator_id'=>$request['operator_id'],'account_id'=>$request['account_id'],'generation'=>$this->generation,'operations'=>array_map(fn($op)=>array_replace($op,['path'=>realpath($op['path'])]),$normalized)];
    }
    private function appendJournal(array $entry): void { $dir=dirname($this->journalPath); if (!is_dir($dir)&&!mkdir($dir,0700,true)&&!is_dir($dir)) throw new BoundedRepairException('journal_unavailable'); $entry['journal_sha256']=hash('sha256',json_encode($entry,JSON_THROW_ON_ERROR)); if (file_put_contents($this->journalPath,json_encode($entry,JSON_THROW_ON_ERROR)."\n",FILE_APPEND|LOCK_EX)===false) throw new BoundedRepairException('journal_unavailable'); @chmod($this->journalPath,0600); }
    private function isCancelled(?callable $callback): bool { return $callback !== null && (bool)$callback(); }
}

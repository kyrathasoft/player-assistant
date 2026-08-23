namespace PlayerAssistant;

internal sealed record RpolUploadRecoveryResult(
    bool UploadCompleted,
    bool CursorPersisted,
    string RecoveryStage,
    IReadOnlyList<string> Errors)
{
    internal bool Succeeded => UploadCompleted
        && CursorPersisted
        && string.IsNullOrWhiteSpace(RecoveryStage)
        && Errors.Count == 0;
}

internal static class RpolUploadRecoveryTransaction
{
    private static readonly TimeSpan UploadJoinBound = TimeSpan.FromSeconds(30);

    internal static async Task<RpolUploadRecoveryResult> ExecuteAsync(
        Func<Task> writeIntentAsync,
        Func<Task> uploadAsync,
        Func<Task> writeUploadedStageAsync,
        Func<Task> writeCursorAsync,
        Func<Task> cleanupRecoveryAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeIntentAsync);
        ArgumentNullException.ThrowIfNull(uploadAsync);
        ArgumentNullException.ThrowIfNull(writeUploadedStageAsync);
        ArgumentNullException.ThrowIfNull(writeCursorAsync);
        ArgumentNullException.ThrowIfNull(cleanupRecoveryAsync);

        try
        {
            await writeIntentAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("intent-write", ex, uploadCompleted: false, cursorPersisted: false);
        }

        Task uploadTask;
        try { uploadTask = uploadAsync(); }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("upload", ex, uploadCompleted: false, cursorPersisted: false);
        }

        try
        {
            await uploadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await uploadTask.WaitAsync(UploadJoinBound).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RpolCleanupUtility.TrackLateTask(uploadTask);
                return Failure("upload-cancellation-join", ex, uploadCompleted: false, cursorPersisted: false);
            }

            return Failure(
                "upload-cancelled-after-completion",
                new OperationCanceledException("The RPOL upload completed while the publisher cancellation was being processed."),
                uploadCompleted: uploadTask.Status == TaskStatus.RanToCompletion,
                cursorPersisted: false);
        }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("upload", ex, uploadCompleted: false, cursorPersisted: false);
        }

        try
        {
            await writeUploadedStageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("uploaded-stage-write", ex, uploadCompleted: true, cursorPersisted: false);
        }

        try
        {
            await writeCursorAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("cursor-write", ex, uploadCompleted: true, cursorPersisted: false);
        }

        try
        {
            await cleanupRecoveryAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldCapture(ex, cancellationToken))
        {
            return Failure("recovery-cleanup", ex, uploadCompleted: true, cursorPersisted: true);
        }

        return new RpolUploadRecoveryResult(true, true, string.Empty, []);
    }

    private static bool ShouldCapture(Exception exception, CancellationToken cancellationToken)
    {
        return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
    }

    private static RpolUploadRecoveryResult Failure(
        string stage,
        Exception exception,
        bool uploadCompleted,
        bool cursorPersisted)
    {
        return new RpolUploadRecoveryResult(
            uploadCompleted,
            cursorPersisted,
            stage,
            [SensitiveTextRedactionUtility.Redact(exception.Message)]);
    }
}

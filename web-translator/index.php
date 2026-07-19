<?php

declare(strict_types=1);

require_once __DIR__ . '/OrcishTranslator.php';

$usingHttps = isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== '' && $_SERVER['HTTPS'] !== 'off';
session_start([
    'cookie_httponly' => true,
    'cookie_samesite' => 'Lax',
    'cookie_secure' => $usingHttps,
    'use_strict_mode' => true,
]);

header("Content-Security-Policy: default-src 'self'; style-src 'self'; script-src 'self'; form-action 'self'; base-uri 'none'; object-src 'none'");
header('Cache-Control: no-store');
header('Referrer-Policy: same-origin');
header('X-Content-Type-Options: nosniff');

$flash = isset($_SESSION['orcish_translator_flash']) && is_array($_SESSION['orcish_translator_flash'])
    ? $_SESSION['orcish_translator_flash']
    : [];
unset($_SESSION['orcish_translator_flash']);

$input = isset($flash['input']) ? (string)$flash['input'] : '';
$translation = isset($flash['translation']) ? (string)$flash['translation'] : '';
$error = isset($flash['error']) ? (string)$flash['error'] : '';
$termCount = 0;

try {
    $translator = new OrcishTranslator(__DIR__ . '/orcish-lexicon.json');
    $termCount = $translator->getEnglishTermCount();

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $input = isset($_POST['english']) ? trim((string)$_POST['english']) : '';
        if (OrcishTranslator::countWords($input) > OrcishTranslator::MAX_INPUT_WORDS) {
            $error = 'Please limit the English text to 5,000 words.';
        } elseif ($input !== '') {
            $translation = $translator->translateSentence($input);
        }

        $_SESSION['orcish_translator_flash'] = [
            'input' => $input,
            'translation' => $translation,
            'error' => $error,
        ];
        session_write_close();

        $redirectPath = isset($_SERVER['SCRIPT_NAME']) && $_SERVER['SCRIPT_NAME'] !== ''
            ? (string)$_SERVER['SCRIPT_NAME']
            : 'index.php';
        header('Location: ' . $redirectPath, true, 303);
        exit;
    }
} catch (Throwable $exception) {
    error_log($exception->getMessage());
    $error = 'The translator is temporarily unavailable.';
}

function escapeHtml(string $value): string
{
    return htmlspecialchars($value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>English to Orcish Translator</title>
    <meta name="description" content="Translate English words and sentences into Orcish.">
    <link rel="stylesheet" href="styles.css?v=20260718-2">
    <script src="translator.js" defer></script>
</head>
<body>
    <main class="translator-shell">
        <header>
            <p class="eyebrow">BryanMiller.us</p>
            <h1>English to Orcish</h1>
            <p class="intro">Enter an English word, phrase, or sentence. Unknown words remain unchanged.</p>
        </header>

        <?php if ($error !== ''): ?>
            <p class="message error" role="alert"><?= escapeHtml($error) ?></p>
        <?php endif; ?>

        <form method="post" action="" class="translator-form">
            <label for="english">English text</label>
            <textarea id="english" name="english" rows="7" data-max-words="<?= OrcishTranslator::MAX_INPUT_WORDS ?>" aria-describedby="english-limit english-word-count" required><?= escapeHtml($input) ?></textarea>
            <div class="input-guidance">
                <span id="english-limit">Maximum 5,000 words. Additional words will not be accepted.</span>
                <span id="english-word-count" class="word-count" aria-live="polite"><?= number_format(OrcishTranslator::countWords($input)) ?> / 5,000 words</span>
            </div>
            <button type="submit">Translate to Orcish</button>
        </form>

        <section class="result" aria-live="polite">
            <label for="orcish" class="result-title">Orcish translation</label>
            <textarea id="orcish" rows="7" readonly><?= escapeHtml($translation) ?></textarea>
        </section>

        <footer>
            <span><?= number_format($termCount) ?> known English terms</span>
            <div class="footer-links">
                <div class="footer-link-row">
                    <a href="orcish-lexicon.json" download>Download the Orcish lexicon (JSON)</a>
                </div>
                <?php if ($translation !== ''): ?>
                    <div class="footer-link-row">
                        <a id="download-orcish" href="#" download="orcish-translation.txt">Download the Orcish translation (TXT)</a>
                    </div>
                <?php endif; ?>
            </div>
        </footer>
    </main>
</body>
</html>

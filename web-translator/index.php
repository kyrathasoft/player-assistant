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
$untranslatedWords = isset($flash['untranslatedWords']) && is_array($flash['untranslatedWords'])
    ? array_values(array_filter($flash['untranslatedWords'], 'is_string'))
    : [];
$error = isset($flash['error']) ? (string)$flash['error'] : '';
$orcishToEnglish = isset($flash['orcishToEnglish']) && $flash['orcishToEnglish'] === true;
$termCount = 0;

try {
    $translator = new OrcishTranslator(__DIR__ . '/orcish-lexicon.json');
    $termCount = $translator->getEnglishTermCount();

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $orcishToEnglish = isset($_POST['orcish_to_english'])
            && (string)$_POST['orcish_to_english'] === '1';
        $input = isset($_POST['english']) ? trim((string)$_POST['english']) : '';
        if (OrcishTranslator::countWords($input) > OrcishTranslator::MAX_INPUT_WORDS) {
            $inputLanguage = $orcishToEnglish ? 'Orcish' : 'English';
            $error = "Please limit the {$inputLanguage} text to 5,000 words.";
        } elseif ($input !== '') {
            $result = $orcishToEnglish
                ? $translator->translateOrcishSentenceWithUnknownWords($input)
                : $translator->translateSentenceWithUnknownWords($input);
            $translation = $result['translation'];
            $untranslatedWords = $result['untranslatedWords'];
        }

        $_SESSION['orcish_translator_flash'] = [
            'input' => $input,
            'translation' => $translation,
            'untranslatedWords' => $untranslatedWords,
            'error' => $error,
            'orcishToEnglish' => $orcishToEnglish,
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

$untranslatedWordsJson = json_encode($untranslatedWords, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
if ($untranslatedWordsJson === false) {
    $untranslatedWordsJson = '[]';
}

$pageTitle = $orcishToEnglish ? 'Orcish to English Translator' : 'English to Orcish Translator';
$pageHeading = $orcishToEnglish ? 'Orcish to English' : 'English to Orcish';
$pageDescription = $orcishToEnglish
    ? 'Translate Orcish words and sentences into English.'
    : 'Translate English words and sentences into Orcish.';
$intro = $orcishToEnglish
    ? 'Enter an Orcish word, phrase, or sentence. Unknown words remain unchanged.'
    : 'Enter an English word, phrase, or sentence. Unknown words remain unchanged.';
$inputLabel = $orcishToEnglish ? 'Orcish text' : 'English text';
$buttonLabel = $orcishToEnglish ? 'Translate to English' : 'Translate to Orcish';
$resultLabel = $orcishToEnglish ? 'English translation' : 'Orcish translation';
$downloadLabel = $orcishToEnglish ? 'Download the English translation (TXT)' : 'Download the Orcish translation (TXT)';
$downloadFilename = $orcishToEnglish ? 'english-translation.txt' : 'orcish-translation.txt';
$untranslatedFilename = $orcishToEnglish ? 'untranslated-orcish-words.txt' : 'untranslated-words.txt';
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title><?= escapeHtml($pageTitle) ?></title>
    <meta name="description" content="<?= escapeHtml($pageDescription) ?>">
    <link rel="stylesheet" href="styles.css?v=20260721-1">
    <script src="translator.js?v=20260719-1" defer></script>
</head>
<body>
    <main class="translator-shell">
        <header>
            <p class="eyebrow">BryanMiller.us</p>
            <h1 id="page-heading"><?= escapeHtml($pageHeading) ?></h1>
            <p id="translator-intro" class="intro"><?= escapeHtml($intro) ?></p>
        </header>

        <?php if ($error !== ''): ?>
            <p class="message error" role="alert"><?= escapeHtml($error) ?></p>
        <?php endif; ?>

        <form method="post" action="" class="translator-form">
            <label class="direction-toggle" for="orcish-to-english">
                <input id="orcish-to-english" name="orcish_to_english" type="checkbox" value="1"<?= $orcishToEnglish ? ' checked' : '' ?>>
                <span>Orcish to English</span>
            </label>
            <label id="source-text-label" for="source-text"><?= escapeHtml($inputLabel) ?></label>
            <textarea id="source-text" name="english" rows="7" data-max-words="<?= OrcishTranslator::MAX_INPUT_WORDS ?>" aria-describedby="source-limit source-word-count" required><?= escapeHtml($input) ?></textarea>
            <div class="input-guidance">
                <span id="source-limit">Maximum 5,000 words. Additional words will not be accepted.</span>
                <span id="source-word-count" class="word-count" aria-live="polite"><?= number_format(OrcishTranslator::countWords($input)) ?> / 5,000 words</span>
            </div>
            <button id="translate-button" type="submit"><?= escapeHtml($buttonLabel) ?></button>
        </form>

        <?php if ($translation !== ''): ?>
            <section id="translation-result" class="result" aria-live="polite">
                <label id="translation-result-label" for="translated-text" class="result-title"><?= escapeHtml($resultLabel) ?></label>
                <textarea id="translated-text" rows="7" readonly><?= escapeHtml($translation) ?></textarea>
            </section>
        <?php endif; ?>

        <footer>
            <span><?= number_format($termCount) ?> known English terms</span>
            <div class="footer-links">
                <div class="footer-link-row">
                    <a href="orcish-lexicon.json" download>Download the Orcish lexicon (JSON)</a>
                </div>
                <?php if ($translation !== ''): ?>
                    <div class="footer-link-row">
                        <a id="download-translation" href="#" download="<?= escapeHtml($downloadFilename) ?>"><?= escapeHtml($downloadLabel) ?></a>
                    </div>
                <?php endif; ?>
                <?php if (count($untranslatedWords) > 0): ?>
                    <div class="footer-link-row footer-link-row-with-note">
                        <a id="download-untranslated" href="#" download="<?= escapeHtml($untranslatedFilename) ?>" data-words="<?= escapeHtml($untranslatedWordsJson) ?>">Download words that couldn't be translated</a>
                        <span class="download-note">(consider emailing this list to kyrathasoft@gmail.com)</span>
                    </div>
                <?php endif; ?>
            </div>
        </footer>
    </main>
</body>
</html>

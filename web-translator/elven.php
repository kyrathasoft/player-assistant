<?php

declare(strict_types=1);

require_once __DIR__ . '/ElvenTranslator.php';

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

$flash = isset($_SESSION['elven_translator_flash']) && is_array($_SESSION['elven_translator_flash'])
    ? $_SESSION['elven_translator_flash']
    : [];
unset($_SESSION['elven_translator_flash']);

$input = isset($flash['input']) ? (string)$flash['input'] : '';
$translation = isset($flash['translation']) ? (string)$flash['translation'] : '';
$untranslatedWords = isset($flash['untranslatedWords']) && is_array($flash['untranslatedWords'])
    ? array_values(array_filter($flash['untranslatedWords'], 'is_string'))
    : [];
$error = isset($flash['error']) ? (string)$flash['error'] : '';
$elvenToEnglish = isset($flash['elvenToEnglish']) && $flash['elvenToEnglish'] === true;
$termCount = 0;

try {
    $translator = new ElvenTranslator(__DIR__ . '/elvish-lexicon.json');
    $termCount = $translator->getEnglishTermCount();

    if ($_SERVER['REQUEST_METHOD'] === 'POST') {
        $elvenToEnglish = isset($_POST['elven_to_english'])
            && (string)$_POST['elven_to_english'] === '1';
        $input = isset($_POST['english']) ? trim((string)$_POST['english']) : '';
        if (ElvenTranslator::countWords($input) > ElvenTranslator::MAX_INPUT_WORDS) {
            $inputLanguage = $elvenToEnglish ? 'Elvish' : 'English';
            $error = "Please limit the {$inputLanguage} text to 5,000 words.";
        } elseif ($input !== '') {
            $result = $elvenToEnglish
                ? $translator->translateElvenSentenceWithUnknownWords($input)
                : $translator->translateSentenceWithUnknownWords($input);
            $translation = $result['translation'];
            $untranslatedWords = $result['untranslatedWords'];
        }

        $_SESSION['elven_translator_flash'] = [
            'input' => $input,
            'translation' => $translation,
            'untranslatedWords' => $untranslatedWords,
            'error' => $error,
            'elvenToEnglish' => $elvenToEnglish,
        ];
        session_write_close();

        $redirectPath = isset($_SERVER['SCRIPT_NAME']) && $_SERVER['SCRIPT_NAME'] !== ''
            ? (string)$_SERVER['SCRIPT_NAME']
            : 'elven.php';
        header('Location: ' . $redirectPath, true, 303);
        exit;
    }
} catch (Throwable $exception) {
    error_log($exception->getMessage());
    $error = 'The translator is temporarily unavailable.';
}

function escapeElvenHtml(string $value): string
{
    return htmlspecialchars($value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

$untranslatedWordsJson = json_encode($untranslatedWords, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
if ($untranslatedWordsJson === false) {
    $untranslatedWordsJson = '[]';
}

$pageTitle = $elvenToEnglish ? 'Elvish to English Translator' : 'English to Elvish Translator';
$pageHeading = $elvenToEnglish ? 'Elvish to English' : 'English to Elvish';
$pageDescription = $elvenToEnglish
    ? 'Translate Elvish words and sentences into English.'
    : 'Translate English words and sentences into Elvish.';
$intro = $elvenToEnglish
    ? 'Enter an Elvish word, phrase, or sentence. Unknown words remain unchanged.'
    : 'Enter an English word, phrase, or sentence. Unknown words remain unchanged.';
$inputLabel = $elvenToEnglish ? 'Elvish text' : 'English text';
$buttonLabel = $elvenToEnglish ? 'Translate to English' : 'Translate to Elvish';
$resultLabel = $elvenToEnglish ? 'English translation' : 'Elvish translation';
$downloadLabel = $elvenToEnglish ? 'Download the English translation (TXT)' : 'Download the Elvish translation (TXT)';
$downloadFilename = $elvenToEnglish ? 'english-translation.txt' : 'elvish-translation.txt';
$untranslatedFilename = $elvenToEnglish ? 'untranslated-elvish-words.txt' : 'untranslated-words.txt';
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title><?= escapeElvenHtml($pageTitle) ?></title>
    <meta name="description" content="<?= escapeElvenHtml($pageDescription) ?>">
    <link rel="stylesheet" href="styles.css?v=20260722-1">
    <script src="translator.js?v=20260722-1" defer></script>
</head>
<body>
    <main class="translator-shell" data-translator-language="Elvish">
        <nav class="translator-nav" aria-label="Translator navigation">
            <a href="index.php">Choose translator</a>
            <a href="orcish.php">Orcish translator</a>
        </nav>
        <header>
            <p class="eyebrow">BryanMiller.us</p>
            <h1 id="page-heading"><?= escapeElvenHtml($pageHeading) ?></h1>
            <p id="translator-intro" class="intro"><?= escapeElvenHtml($intro) ?></p>
        </header>

        <?php if ($error !== ''): ?>
            <p class="message error" role="alert"><?= escapeElvenHtml($error) ?></p>
        <?php endif; ?>

        <form method="post" action="" class="translator-form">
            <label class="direction-toggle" for="elven-to-english">
                <input id="elven-to-english" name="elven_to_english" type="checkbox" value="1" data-direction-toggle<?= $elvenToEnglish ? ' checked' : '' ?>>
                <span>Elvish to English</span>
            </label>
            <label id="source-text-label" for="source-text"><?= escapeElvenHtml($inputLabel) ?></label>
            <textarea id="source-text" name="english" rows="7" data-max-words="<?= ElvenTranslator::MAX_INPUT_WORDS ?>" aria-describedby="source-limit source-word-count" required><?= escapeElvenHtml($input) ?></textarea>
            <div class="input-guidance">
                <span id="source-limit">Maximum 5,000 words. Additional words will not be accepted.</span>
                <span id="source-word-count" class="word-count" aria-live="polite"><?= number_format(ElvenTranslator::countWords($input)) ?> / 5,000 words</span>
            </div>
            <button id="translate-button" type="submit"><?= escapeElvenHtml($buttonLabel) ?></button>
        </form>

        <?php if ($translation !== ''): ?>
            <section id="translation-result" class="result" aria-live="polite">
                <label id="translation-result-label" for="translated-text" class="result-title"><?= escapeElvenHtml($resultLabel) ?></label>
                <textarea id="translated-text" rows="7" readonly><?= escapeElvenHtml($translation) ?></textarea>
            </section>
        <?php endif; ?>

        <footer>
            <span><?= number_format($termCount) ?> known English terms</span>
            <div class="footer-links">
                <div class="footer-link-row">
                    <a href="elvish-lexicon.json" download>Download the Elvish lexicon (JSON)</a>
                </div>
                <?php if ($translation !== ''): ?>
                    <div class="footer-link-row">
                        <a id="download-translation" href="#" download="<?= escapeElvenHtml($downloadFilename) ?>"><?= escapeElvenHtml($downloadLabel) ?></a>
                    </div>
                <?php endif; ?>
                <?php if (count($untranslatedWords) > 0): ?>
                    <div class="footer-link-row footer-link-row-with-note">
                        <a id="download-untranslated" href="#" download="<?= escapeElvenHtml($untranslatedFilename) ?>" data-words="<?= escapeElvenHtml($untranslatedWordsJson) ?>">Download words that couldn't be translated</a>
                        <span class="download-note">(consider emailing this list to kyrathasoft@gmail.com)</span>
                    </div>
                <?php endif; ?>
            </div>
        </footer>

        <p class="source-note">Sindarin is preferred; Quenya is used where no Sindarin equivalent is available. Source vocabulary includes Eldamo 0.8.13 under CC BY 4.0, supplemented by validator-reviewed project forms.</p>
    </main>
</body>
</html>

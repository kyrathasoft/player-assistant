<?php

declare(strict_types=1);

header("Content-Security-Policy: default-src 'self'; style-src 'self'; script-src 'self'; base-uri 'none'; object-src 'none'");
header('Cache-Control: no-store');
header('Referrer-Policy: same-origin');
header('X-Content-Type-Options: nosniff');
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Fantasy Language Translators</title>
    <meta name="description" content="Choose the English–Orcish or English–Elvish translator.">
    <link rel="stylesheet" href="styles.css?v=20260722-1">
</head>
<body>
    <main class="translator-shell translator-choice-shell">
        <header>
            <p class="eyebrow">BryanMiller.us</p>
            <h1>Choose a translator</h1>
            <p class="intro">Translate words, phrases, and sentences between English and one of the campaign’s fantasy languages.</p>
        </header>

        <section class="translator-choices" aria-label="Available translators">
            <a class="translator-choice orcish-choice" href="orcish.php">
                <span class="choice-kicker">Black Speech inspired</span>
                <strong>Orcish Translator</strong>
                <span>English ↔ Orcish</span>
                <span class="choice-action">Open translator →</span>
            </a>
            <a class="translator-choice elven-choice" href="elven.php">
                <span class="choice-kicker">Sindarin preferred</span>
                <strong>Elven Translator</strong>
                <span>English ↔ Elvish</span>
                <span class="choice-action">Open translator →</span>
            </a>
        </section>

        <footer class="choice-footer">
            <span>Unknown words remain unchanged.</span>
        </footer>
    </main>
</body>
</html>

<?php

declare(strict_types=1);

final class RpolClient
{
    private const ALLOWED_QUERY_KEYS = [
        '/game.php' => ['gi', 'date', 'filter'],
        '/gameinfo.php' => ['gi', 'action', 'ci'],
        '/display.cgi' => ['gi', 'ti', 'date', 'msgpage', 'show', 'new'],
        '/usermodules/diceroller.cgi' => ['gi'],
    ];

    private CurlHandle $curl;
    private bool $authenticated = false;

    public function __construct(private readonly array $config)
    {
        if (!extension_loaded('curl')) {
            throw new RuntimeException('The PHP cURL extension is required.');
        }

        $curl = curl_init();
        if ($curl === false) {
            throw new RuntimeException('Unable to initialize PHP cURL.');
        }

        $this->curl = $curl;
        curl_setopt($this->curl, CURLOPT_COOKIEFILE, '');
    }


    public function fetchPage(string $url): array
    {
        $this->validateTargetUrl($url);
        $this->ensureConfigured();
        $this->ensureAuthenticated();

        $response = $this->requestFollowingRedirects(
            'GET',
            $url,
            null,
            [],
            function (string $redirectUrl): void {
                $this->validateRedirectUrl($redirectUrl, true);
            });
        if ($this->looksLikeLoginPage($response['body'])) {
            throw new RuntimeException('RPOL returned a login page after authentication.');
        }

        if ($this->looksLikeCloudflareChallenge($response['body'])) {
            throw new RuntimeException('RPOL requires browser verification that PHP cURL cannot complete.');
        }

        $contentType = strtolower((string)($response['headers']['content-type'] ?? ''));
        if (!str_contains($contentType, 'text/html')) {
            throw new RuntimeException('RPOL returned an unexpected content type.');
        }

        return [
            'url' => $response['url'],
            'content_type' => $contentType,
            'html' => $response['body'],
        ];
    }

    private function ensureAuthenticated(): void
    {
        if ($this->authenticated) {
            return;
        }

        $initialUrl = (string)$this->config['initial_url'];
        $this->validateRedirectUrl($initialUrl, true);
        $loginPage = $this->requestFollowingRedirects(
            'GET',
            $initialUrl,
            null,
            [],
            function (string $redirectUrl): void {
                $this->validateRedirectUrl($redirectUrl, true);
            });
        if ($this->looksLikeCloudflareChallenge($loginPage['body'])) {
            throw new RuntimeException('RPOL requires browser verification that PHP cURL cannot complete.');
        }

        if (!$this->looksLikeLoginPage($loginPage['body'])) {
            $this->authenticated = true;
            return;
        }

        [$actionUrl, $fields] = $this->readLoginForm($loginPage['body'], $loginPage['url']);
        $fields['username'] = (string)$this->config['username'];
        $fields['password'] = (string)$this->config['password'];
        $fields['perm'] = '1';
        $fields['specialaction'] = 'Login';

        $loginResponse = $this->requestFollowingRedirects(
            'POST',
            $actionUrl,
            http_build_query($fields, '', '&', PHP_QUERY_RFC3986),
            ['Content-Type: application/x-www-form-urlencoded'],
            function (string $redirectUrl): void {
                $this->validateRedirectUrl($redirectUrl, true);
            });

        if ($this->looksLikeLoginPage($loginResponse['body'])) {
            throw new RuntimeException('RPOL rejected the configured credentials.');
        }

        if ($this->looksLikeCloudflareChallenge($loginResponse['body'])) {
            throw new RuntimeException('RPOL requires browser verification that PHP cURL cannot complete.');
        }

        $this->authenticated = true;
    }

    private function readLoginForm(string $html, string $pageUrl): array
    {
        if (!class_exists(DOMDocument::class)) {
            throw new RuntimeException('The PHP DOM extension is required to parse the RPOL login form.');
        }

        $previousUseErrors = libxml_use_internal_errors(true);
        try {
            $document = new DOMDocument();
            if (!$document->loadHTML($html, LIBXML_NONET | LIBXML_NOERROR | LIBXML_NOWARNING)) {
                throw new RuntimeException('Unable to parse the RPOL login form.');
            }

            $xpath = new DOMXPath($document);
            $forms = $xpath->query("//form[.//input[@name='username'] and .//input[@name='password']]");
            if ($forms === false || $forms->length !== 1) {
                throw new RuntimeException('RPOL did not return one recognizable login form.');
            }

            $form = $forms->item(0);
            if (!$form instanceof DOMElement) {
                throw new RuntimeException('RPOL returned an invalid login form.');
            }

            $actionUrl = $this->resolveUrl($pageUrl, $form->getAttribute('action'));
            $this->validateTransportUrl($actionUrl);
            if (parse_url($actionUrl, PHP_URL_PATH) !== '/login.cgi') {
                throw new RuntimeException('RPOL returned an unexpected login endpoint.');
            }

            $fields = [];
            $inputs = $xpath->query('.//input[@name]', $form);
            if ($inputs !== false) {
                foreach ($inputs as $input) {
                    if (!$input instanceof DOMElement || $input->hasAttribute('disabled')) {
                        continue;
                    }

                    $name = $input->getAttribute('name');
                    $type = strtolower($input->getAttribute('type'));
                    if ($name === '' || in_array($type, ['file', 'reset'], true)) {
                        continue;
                    }

                    if (in_array($type, ['checkbox', 'radio'], true) && !$input->hasAttribute('checked')) {
                        continue;
                    }

                    if ($type === 'submit' && $name !== 'specialaction') {
                        continue;
                    }

                    $fields[$name] = $input->getAttribute('value');
                }
            }

            return [$actionUrl, $fields];
        } finally {
            libxml_clear_errors();
            libxml_use_internal_errors($previousUseErrors);
        }
    }

    private function requestFollowingRedirects(
        string $method,
        string $url,
        ?string $body = null,
        array $headers = [],
        ?callable $redirectValidator = null): array
    {
        $redirectValidator ??= function (string $redirectUrl): void {
            $this->validateTransportUrl($redirectUrl);
        };
        for ($redirectCount = 0; $redirectCount <= 5; $redirectCount++) {
            $this->validateTransportUrl($url);
            $response = $this->request($method, $url, $body, $headers);
            if (!in_array($response['status'], [301, 302, 303, 307, 308], true)) {
                if ($response['status'] < 200 || $response['status'] >= 400) {
                    throw new RuntimeException('RPOL returned HTTP status ' . $response['status'] . '.');
                }

                return $response;
            }

            $location = $response['headers']['location'] ?? null;
            if (!is_string($location) || $location === '') {
                throw new RuntimeException('RPOL returned a redirect without a Location header.');
            }

            $url = $this->resolveUrl($url, $location);
            $redirectValidator($url);
            if (in_array($response['status'], [301, 302, 303], true)) {
                $method = 'GET';
                $body = null;
                $headers = [];
            }
        }

        throw new RuntimeException('RPOL exceeded the redirect limit.');
    }

    private function validateRedirectUrl(string $url, bool $allowLoginEndpoint): void
    {
        $path = (string)parse_url($url, PHP_URL_PATH);
        if ($allowLoginEndpoint && $path === '/login.cgi'
            && (string)parse_url($url, PHP_URL_QUERY) === '') {
            $this->validateTransportUrl($url);
            return;
        }

        $this->validateTargetUrl($url);
    }

    private function request(string $method, string $url, ?string $body, array $headers): array
    {
        $responseHeaders = [];
        $responseBody = '';
        $maxBytes = (int)$this->config['max_response_bytes'];
        $tooLarge = false;

        curl_setopt_array($this->curl, [
            CURLOPT_URL => $url,
            CURLOPT_CUSTOMREQUEST => $method,
            CURLOPT_POST => $method === 'POST',
            CURLOPT_POSTFIELDS => $method === 'POST' ? ($body ?? '') : null,
            CURLOPT_HTTPHEADER => $headers,
            CURLOPT_RETURNTRANSFER => false,
            CURLOPT_FOLLOWLOCATION => false,
            CURLOPT_CONNECTTIMEOUT => (int)$this->config['connect_timeout_seconds'],
            CURLOPT_TIMEOUT => (int)$this->config['request_timeout_seconds'],
            CURLOPT_SSL_VERIFYPEER => true,
            CURLOPT_SSL_VERIFYHOST => 2,
            CURLOPT_PROTOCOLS => CURLPROTO_HTTPS,
            CURLOPT_ENCODING => '',
            CURLOPT_USERAGENT => (string)$this->config['user_agent'],
            CURLOPT_REFERER => $this->isDiceRollerUrl($url)
                ? (string)$this->config['initial_url']
                : '',
            CURLOPT_HEADERFUNCTION => static function (CurlHandle $handle, string $line) use (&$responseHeaders): int {
                $length = strlen($line);
                $trimmed = trim($line);
                if ($trimmed === '' || str_starts_with($trimmed, 'HTTP/')) {
                    return $length;
                }

                $separator = strpos($trimmed, ':');
                if ($separator !== false) {
                    $name = strtolower(trim(substr($trimmed, 0, $separator)));
                    $responseHeaders[$name] = trim(substr($trimmed, $separator + 1));
                }

                return $length;
            },
            CURLOPT_WRITEFUNCTION => static function (CurlHandle $handle, string $chunk) use (&$responseBody, &$tooLarge, $maxBytes): int {
                if (strlen($responseBody) + strlen($chunk) > $maxBytes) {
                    $tooLarge = true;
                    return 0;
                }

                $responseBody .= $chunk;
                return strlen($chunk);
            },
        ]);

        $result = curl_exec($this->curl);
        if ($result === false) {
            if ($tooLarge) {
                throw new RuntimeException('RPOL response exceeded the configured size limit.');
            }

            throw new RuntimeException('RPOL request failed: ' . curl_error($this->curl));
        }

        return [
            'status' => (int)curl_getinfo($this->curl, CURLINFO_RESPONSE_CODE),
            'url' => (string)curl_getinfo($this->curl, CURLINFO_EFFECTIVE_URL),
            'headers' => $responseHeaders,
            'body' => $responseBody,
        ];
    }

    private function isDiceRollerUrl(string $url): bool
    {
        return parse_url($url, PHP_URL_PATH) === '/usermodules/diceroller.cgi';
    }

    public function validateTargetUrl(string $url): void
    {
        $this->validateTransportUrl($url);
        if (strlen($url) > 2048) {
            throw new InvalidArgumentException('The RPOL URL is too long.');
        }

        $path = (string)parse_url($url, PHP_URL_PATH);
        if (!array_key_exists($path, self::ALLOWED_QUERY_KEYS)) {
            throw new InvalidArgumentException('The RPOL path is not approved for broker access.');
        }

        parse_str((string)(parse_url($url, PHP_URL_QUERY) ?? ''), $query);
        foreach ($query as $key => $value) {
            if (!in_array($key, self::ALLOWED_QUERY_KEYS[$path], true) || is_array($value)) {
                throw new InvalidArgumentException('The RPOL URL contains an unsupported query parameter.');
            }
        }

        if (($query['gi'] ?? null) !== (string)$this->config['game_id']) {
            throw new InvalidArgumentException('The RPOL URL does not target the configured game.');
        }

        if ($path === '/gameinfo.php' && isset($query['action'])) {
            $allowedActions = ['cast', 'gamelinks', 'viewmap', 'viewdescription'];
            if (!in_array($query['action'], $allowedActions, true)) {
                throw new InvalidArgumentException('The RPOL game-info action is not approved.');
            }
        }
    }

    private function validateTransportUrl(string $url): void
    {
        $parts = parse_url($url);
        if (!is_array($parts)
            || strtolower((string)($parts['scheme'] ?? '')) !== 'https'
            || strtolower((string)($parts['host'] ?? '')) !== 'rpol.net'
            || (isset($parts['port']) && (int)$parts['port'] !== 443)
            || isset($parts['user'])
            || isset($parts['pass'])
            || isset($parts['fragment'])) {
            throw new InvalidArgumentException('Only credential-free HTTPS URLs on rpol.net are allowed.');
        }
    }

    private function resolveUrl(string $baseUrl, string $location): string
    {
        if (preg_match('#^https://#i', $location) === 1) {
            return $location;
        }

        $base = parse_url($baseUrl);
        if (!is_array($base) || !isset($base['scheme'], $base['host'])) {
            throw new RuntimeException('Unable to resolve an RPOL redirect URL.');
        }

        if (str_starts_with($location, '//')) {
            return $base['scheme'] . ':' . $location;
        }

        if (str_starts_with($location, '/')) {
            return $base['scheme'] . '://' . $base['host'] . $location;
        }

        $basePath = (string)($base['path'] ?? '/');
        $directory = rtrim(str_replace('\\', '/', dirname($basePath)), '/');
        return $base['scheme'] . '://' . $base['host'] . ($directory === '' ? '' : $directory) . '/' . $location;
    }

    private function looksLikeLoginPage(string $html): bool
    {
        return stripos($html, "action='/login.cgi'") !== false
            && stripos($html, "name='username'") !== false
            && stripos($html, "name='password'") !== false;
    }

    private function looksLikeCloudflareChallenge(string $html): bool
    {
        return stripos($html, 'cf-chl-') !== false
            || stripos($html, 'challenge-platform') !== false
            || stripos($html, '<title>Just a moment...</title>') !== false;
    }

    private function ensureConfigured(): void
    {
        foreach (['username', 'password'] as $key) {
            $value = (string)($this->config[$key] ?? '');
            if ($value === '' || str_starts_with($value, 'CHANGE_ME')) {
                throw new RuntimeException('The private RPOL credentials have not been configured.');
            }
        }
    }
}

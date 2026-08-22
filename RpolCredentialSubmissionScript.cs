namespace PlayerAssistant;

internal static class RpolCredentialSubmissionScript
{
    internal const string Source = """
        (form, credentials) => {
            const isExactTrustedForm = () => {
                const live = new URL(location.href);
                const action = new URL(form.action, live);
                return window.top === window.self
                    && live.protocol === 'https:'
                    && live.hostname.toLowerCase() === 'rpol.net'
                    && (live.port === '' || live.port === '443')
                    && live.pathname === '/game.php'
                    && live.hash === ''
                    && document.forms.length === 1
                    && document.forms[0] === form
                    && form.isConnected
                    && action.protocol === 'https:'
                    && action.hostname.toLowerCase() === 'rpol.net'
                    && (action.port === '' || action.port === '443')
                    && action.pathname === '/login.cgi'
                    && action.search === ''
                    && action.hash === ''
                    && form.method.toUpperCase() === 'POST'
                    && (!form.target || form.target.toLowerCase() === '_self');
            };
            const userInput = form.querySelector("input[name='username']");
            const passwordInput = form.querySelector("input[name='password']");
            if (!isExactTrustedForm() || !userInput || !passwordInput) return false;
            userInput.value = credentials.userName;
            passwordInput.value = credentials.password;
            userInput.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            if (!isExactTrustedForm()) return false;
            userInput.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
            if (!isExactTrustedForm()) return false;
            passwordInput.dispatchEvent(new Event('input', { bubbles: true, cancelable: true }));
            if (!isExactTrustedForm()) return false;
            passwordInput.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
            if (!isExactTrustedForm()) return false;
            const remember = form.querySelector("input[name='perm']");
            if (remember && !remember.checked) {
                remember.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                if (!isExactTrustedForm()) return false;
            }
            const submitButton = form.querySelector("input[name='specialaction'][value='Login']");
            if (!submitButton || !isExactTrustedForm()) return false;
            const preventNativeSubmit = event => {
                if (event.target === submitButton) event.preventDefault();
            };
            form.addEventListener('click', preventNativeSubmit, true);
            submitButton.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            form.removeEventListener('click', preventNativeSubmit, true);
            if (!isExactTrustedForm()) return false;
            const submitEvent = new Event('submit', { bubbles: true, cancelable: true });
            if (!form.dispatchEvent(submitEvent) || !isExactTrustedForm()) return false;
            HTMLFormElement.prototype.submit.call(form);
            return true;
        }
        """;
}

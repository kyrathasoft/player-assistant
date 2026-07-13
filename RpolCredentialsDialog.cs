namespace PlayerAssistant
{
    internal sealed class RpolCredentialsDialog : Form
    {
        private readonly TextBox _userNameTextBox;
        private readonly TextBox _passwordTextBox;

        public RpolCredentialsDialog(string? userName, string? password)
        {
            Text = "RPOL Credentials";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 166);

            var userNameLabel = new Label
            {
                Left = 16,
                Top = 20,
                Width = 120,
                Text = "User name"
            };

            _userNameTextBox = new TextBox
            {
                Left = 144,
                Top = 16,
                Width = 248,
                Text = userName ?? string.Empty
            };

            var passwordLabel = new Label
            {
                Left = 16,
                Top = 58,
                Width = 120,
                Text = "Password"
            };

            _passwordTextBox = new TextBox
            {
                Left = 144,
                Top = 54,
                Width = 248,
                UseSystemPasswordChar = true,
                Text = password ?? string.Empty
            };

            var noteLabel = new Label
            {
                Left = 16,
                Top = 88,
                Width = 376,
                Height = 30,
                Text = "Credentials are stored in Windows Credential Manager for this Windows user."
            };

            var saveButton = new Button
            {
                Left = 144,
                Top = 126,
                Width = 80,
                Text = "Save",
                DialogResult = DialogResult.OK
            };

            var removeButton = new Button
            {
                Left = 232,
                Top = 126,
                Width = 80,
                Text = "Remove",
                DialogResult = DialogResult.Abort
            };

            var cancelButton = new Button
            {
                Left = 320,
                Top = 126,
                Width = 72,
                Text = "Cancel",
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(
            [
                userNameLabel,
                _userNameTextBox,
                passwordLabel,
                _passwordTextBox,
                noteLabel,
                saveButton,
                removeButton,
                cancelButton
            ]);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public string UserName => _userNameTextBox.Text.Trim();

        public string Password => _passwordTextBox.Text;
    }
}

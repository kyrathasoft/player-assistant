namespace PlayerAssistant
{
    internal class SearchTextBox : TextBox
    {
        public event EventHandler? EnterPressed;

        protected override bool IsInputKey(Keys keyData)
        {
            var keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Enter)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                EnterPressed?.Invoke(this, EventArgs.Empty);
                return;
            }

            base.OnKeyDown(e);
        }
    }
}

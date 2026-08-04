namespace PlayerAssistant
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            menuStrip = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            exitToolStripMenuItem = new ToolStripMenuItem();
            showToolStripMenuItem = new ToolStripMenuItem();
            loginInfoToolStripMenuItem = new ToolStripMenuItem();
            showPostTotalsToolStripMenuItem = new ToolStripMenuItem();
            showDiceRollsToolStripMenuItem = new ToolStripMenuItem();
            xpToolStripMenuItem = new ToolStripMenuItem();
            partyToolStripMenuItem = new ToolStripMenuItem();
            formerPcsToolStripMenuItem = new ToolStripMenuItem();
            myHeroBriefingToolStripMenuItem = new ToolStripMenuItem();
            adventureOutlineToolStripMenuItem = new ToolStripMenuItem();
            regionalMapToolStripMenuItem = new ToolStripMenuItem();
            translatorToolStripMenuItem = new ToolStripMenuItem();
            orcishTranslatorToolStripMenuItem = new ToolStripMenuItem();
            elvenTranslatorToolStripMenuItem = new ToolStripMenuItem();
            ghukliakTranslatorToolStripMenuItem = new ToolStripMenuItem();
            searchToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            skipHeroImageParadeAtStartupToolStripMenuItem = new ToolStripMenuItem();
            whiteMarbleBackgroundTilingToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            authorToolStripMenuItem = new ToolStripMenuItem();
            checkForUpdateToolStripMenuItem = new ToolStripMenuItem();
            versionToolStripMenuItem = new ToolStripMenuItem();
            statusStrip = new StatusStrip();
            statusToolStripStatusLabel = new ToolStripStatusLabel();
            statusActivityToolStripStatusLabel = new ToolStripStatusLabel();
            pnlSearch = new Panel();
            pnlSearchScope = new Panel();
            rdoObsidian = new RadioButton();
            rdoRPOL = new RadioButton();
            rdoSearchDefault = new RadioButton();
            pnlSearchResults = new Panel();
            lstSearchResults = new ListBox();
            btnSearch = new Button();
            lblSearchCharacterCnt = new Label();
            txtSearch = new SearchTextBox();
            lblSearchPrompt = new Label();
            menuStrip.SuspendLayout();
            statusStrip.SuspendLayout();
            pnlSearch.SuspendLayout();
            pnlSearchScope.SuspendLayout();
            pnlSearchResults.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, showToolStripMenuItem, searchToolStripMenuItem, settingsToolStripMenuItem, aboutToolStripMenuItem });
            menuStrip.Location = new Point(0, 0);
            menuStrip.Name = "menuStrip";
            menuStrip.Size = new Size(1024, 24);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exitToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new Size(92, 22);
            exitToolStripMenuItem.Text = "Exit";
            exitToolStripMenuItem.Click += ExitToolStripMenuItem_Click;
            // 
            // showToolStripMenuItem
            // 
            showToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { loginInfoToolStripMenuItem, showPostTotalsToolStripMenuItem, showDiceRollsToolStripMenuItem, xpToolStripMenuItem, partyToolStripMenuItem, formerPcsToolStripMenuItem, myHeroBriefingToolStripMenuItem, adventureOutlineToolStripMenuItem, regionalMapToolStripMenuItem, translatorToolStripMenuItem });
            showToolStripMenuItem.Name = "showToolStripMenuItem";
            showToolStripMenuItem.Size = new Size(48, 20);
            showToolStripMenuItem.Text = "Show";
            showToolStripMenuItem.DropDownOpening += FileToolStripMenuItem_DropDownOpening;
            // 
            // loginInfoToolStripMenuItem
            // 
            loginInfoToolStripMenuItem.Name = "loginInfoToolStripMenuItem";
            loginInfoToolStripMenuItem.Size = new Size(163, 22);
            loginInfoToolStripMenuItem.Text = "Login Info";
            loginInfoToolStripMenuItem.Click += LoginInfoToolStripMenuItem_Click;
            // 
            // showPostTotalsToolStripMenuItem
            // 
            showPostTotalsToolStripMenuItem.Name = "showPostTotalsToolStripMenuItem";
            showPostTotalsToolStripMenuItem.Size = new Size(163, 22);
            showPostTotalsToolStripMenuItem.Text = "Show Post Totals";
            showPostTotalsToolStripMenuItem.Click += ShowPostTotalsToolStripMenuItem_Click;
            // 
            // showDiceRollsToolStripMenuItem
            // 
            showDiceRollsToolStripMenuItem.Name = "showDiceRollsToolStripMenuItem";
            showDiceRollsToolStripMenuItem.Size = new Size(163, 22);
            showDiceRollsToolStripMenuItem.Text = "Show Dice Rolls";
            showDiceRollsToolStripMenuItem.Click += ShowDiceRollsToolStripMenuItem_Click;
            //
            // xpToolStripMenuItem
            //
            xpToolStripMenuItem.Name = "xpToolStripMenuItem";
            xpToolStripMenuItem.Size = new Size(163, 22);
            xpToolStripMenuItem.Text = "XP";
            xpToolStripMenuItem.Click += XpToolStripMenuItem_Click;
            // 
            // partyToolStripMenuItem
            // 
            partyToolStripMenuItem.Name = "partyToolStripMenuItem";
            partyToolStripMenuItem.Size = new Size(163, 22);
            partyToolStripMenuItem.Text = "Party";
            partyToolStripMenuItem.Click += PartyToolStripMenuItem_Click;
            //
            // formerPcsToolStripMenuItem
            //
            formerPcsToolStripMenuItem.Name = "formerPcsToolStripMenuItem";
            formerPcsToolStripMenuItem.Size = new Size(163, 22);
            formerPcsToolStripMenuItem.Text = "Former PCs";
            formerPcsToolStripMenuItem.Click += FormerPcsToolStripMenuItem_Click;
            // 
            // myHeroBriefingToolStripMenuItem
            // 
            myHeroBriefingToolStripMenuItem.Name = "myHeroBriefingToolStripMenuItem";
            myHeroBriefingToolStripMenuItem.Size = new Size(163, 22);
            myHeroBriefingToolStripMenuItem.Text = "My Hero Briefing";
            myHeroBriefingToolStripMenuItem.Click += MyHeroBriefingToolStripMenuItem_Click;
            // 
            // adventureOutlineToolStripMenuItem
            // 
            adventureOutlineToolStripMenuItem.Name = "adventureOutlineToolStripMenuItem";
            adventureOutlineToolStripMenuItem.Size = new Size(163, 22);
            adventureOutlineToolStripMenuItem.Text = "Adventure Outline";
            adventureOutlineToolStripMenuItem.Click += AdventureOutlineToolStripMenuItem_Click;
            // 
            // regionalMapToolStripMenuItem
            // 
            regionalMapToolStripMenuItem.Enabled = false;
            regionalMapToolStripMenuItem.Name = "regionalMapToolStripMenuItem";
            regionalMapToolStripMenuItem.Size = new Size(163, 22);
            regionalMapToolStripMenuItem.Text = "Regional Map";
            regionalMapToolStripMenuItem.Click += RegionalMapToolStripMenuItem_Click;
            //
            // translatorToolStripMenuItem
            //
            translatorToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { orcishTranslatorToolStripMenuItem, elvenTranslatorToolStripMenuItem, ghukliakTranslatorToolStripMenuItem });
            translatorToolStripMenuItem.Name = "translatorToolStripMenuItem";
            translatorToolStripMenuItem.Size = new Size(163, 22);
            translatorToolStripMenuItem.Text = "Translate";
            //
            // orcishTranslatorToolStripMenuItem
            //
            orcishTranslatorToolStripMenuItem.Name = "orcishTranslatorToolStripMenuItem";
            orcishTranslatorToolStripMenuItem.Size = new Size(180, 22);
            orcishTranslatorToolStripMenuItem.Text = "Orcish";
            orcishTranslatorToolStripMenuItem.Click += OrcishTranslatorToolStripMenuItem_Click;
            //
            // elvenTranslatorToolStripMenuItem
            //
            elvenTranslatorToolStripMenuItem.Name = "elvenTranslatorToolStripMenuItem";
            elvenTranslatorToolStripMenuItem.Size = new Size(180, 22);
            elvenTranslatorToolStripMenuItem.Text = "Elven";
            elvenTranslatorToolStripMenuItem.Click += ElvenTranslatorToolStripMenuItem_Click;
            //
            // ghukliakTranslatorToolStripMenuItem
            //
            ghukliakTranslatorToolStripMenuItem.Name = "ghukliakTranslatorToolStripMenuItem";
            ghukliakTranslatorToolStripMenuItem.Size = new Size(180, 22);
            ghukliakTranslatorToolStripMenuItem.Text = "Goblin (Ghukliak)";
            ghukliakTranslatorToolStripMenuItem.Click += GhukliakTranslatorToolStripMenuItem_Click;
            //
            // searchToolStripMenuItem
            // 
            searchToolStripMenuItem.Name = "searchToolStripMenuItem";
            searchToolStripMenuItem.Size = new Size(54, 20);
            searchToolStripMenuItem.Text = "Search";
            searchToolStripMenuItem.Click += SearchToolStripMenuItem_Click;
            // 
            // settingsToolStripMenuItem
            // 
            settingsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { skipHeroImageParadeAtStartupToolStripMenuItem, whiteMarbleBackgroundTilingToolStripMenuItem });
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new Size(61, 20);
            settingsToolStripMenuItem.Text = "Settings";
            settingsToolStripMenuItem.DropDownOpening += NonSearchToolStripMenuItem_DropDownOpening;
            // 
            // skipHeroImageParadeAtStartupToolStripMenuItem
            // 
            skipHeroImageParadeAtStartupToolStripMenuItem.CheckOnClick = true;
            skipHeroImageParadeAtStartupToolStripMenuItem.Name = "skipHeroImageParadeAtStartupToolStripMenuItem";
            skipHeroImageParadeAtStartupToolStripMenuItem.Size = new Size(256, 22);
            skipHeroImageParadeAtStartupToolStripMenuItem.Text = "Skip Hero Image Parade At Startup";
            skipHeroImageParadeAtStartupToolStripMenuItem.Click += SkipHeroImageParadeAtStartupToolStripMenuItem_Click;
            // 
            // whiteMarbleBackgroundTilingToolStripMenuItem
            // 
            whiteMarbleBackgroundTilingToolStripMenuItem.Checked = true;
            whiteMarbleBackgroundTilingToolStripMenuItem.CheckOnClick = true;
            whiteMarbleBackgroundTilingToolStripMenuItem.CheckState = CheckState.Checked;
            whiteMarbleBackgroundTilingToolStripMenuItem.Name = "whiteMarbleBackgroundTilingToolStripMenuItem";
            whiteMarbleBackgroundTilingToolStripMenuItem.Size = new Size(256, 22);
            whiteMarbleBackgroundTilingToolStripMenuItem.Text = "White Marble Background Tiling";
            whiteMarbleBackgroundTilingToolStripMenuItem.CheckedChanged += WhiteMarbleBackgroundTilingToolStripMenuItem_CheckedChanged;
            whiteMarbleBackgroundTilingToolStripMenuItem.Click += WhiteMarbleBackgroundTilingToolStripMenuItem_Click;
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { authorToolStripMenuItem, checkForUpdateToolStripMenuItem, versionToolStripMenuItem });
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new Size(52, 20);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.DropDownOpening += NonSearchToolStripMenuItem_DropDownOpening;
            // 
            // authorToolStripMenuItem
            // 
            authorToolStripMenuItem.Name = "authorToolStripMenuItem";
            authorToolStripMenuItem.Size = new Size(167, 22);
            authorToolStripMenuItem.Text = "Author";
            authorToolStripMenuItem.Click += AuthorToolStripMenuItem_Click;
            // 
            // checkForUpdateToolStripMenuItem
            // 
            checkForUpdateToolStripMenuItem.Name = "checkForUpdateToolStripMenuItem";
            checkForUpdateToolStripMenuItem.Size = new Size(167, 22);
            checkForUpdateToolStripMenuItem.Text = "Check for Updates";
            checkForUpdateToolStripMenuItem.Click += CheckForUpdateToolStripMenuItem_Click;
            // 
            // versionToolStripMenuItem
            // 
            versionToolStripMenuItem.Name = "versionToolStripMenuItem";
            versionToolStripMenuItem.Size = new Size(167, 22);
            versionToolStripMenuItem.Text = "Version";
            versionToolStripMenuItem.Click += VersionToolStripMenuItem_Click;
            // 
            // statusStrip
            // 
            statusStrip.Items.AddRange(new ToolStripItem[] { statusToolStripStatusLabel, statusActivityToolStripStatusLabel });
            statusStrip.Location = new Point(0, 746);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1024, 22);
            statusStrip.TabIndex = 1;
            statusStrip.Text = "statusStrip";
            // 
            // statusToolStripStatusLabel
            // 
            statusToolStripStatusLabel.Name = "statusToolStripStatusLabel";
            statusToolStripStatusLabel.Size = new Size(39, 17);
            statusToolStripStatusLabel.Text = "Ready";
            // 
            // statusActivityToolStripStatusLabel
            // 
            statusActivityToolStripStatusLabel.AutoSize = false;
            statusActivityToolStripStatusLabel.Available = false;
            statusActivityToolStripStatusLabel.Name = "statusActivityToolStripStatusLabel";
            statusActivityToolStripStatusLabel.Size = new Size(24, 17);
            statusActivityToolStripStatusLabel.Text = "";
            // 
            // pnlSearch
            // 
            pnlSearch.Controls.Add(pnlSearchScope);
            pnlSearch.Controls.Add(pnlSearchResults);
            pnlSearch.Controls.Add(btnSearch);
            pnlSearch.Controls.Add(lblSearchCharacterCnt);
            pnlSearch.Controls.Add(txtSearch);
            pnlSearch.Controls.Add(lblSearchPrompt);
            pnlSearch.Location = new Point(12, 27);
            pnlSearch.Name = "pnlSearch";
            pnlSearch.Size = new Size(994, 738);
            pnlSearch.TabIndex = 2;
            pnlSearch.Visible = false;
            pnlSearch.Paint += PnlSearch_Paint;
            // 
            // pnlSearchScope
            // 
            pnlSearchScope.BackColor = Color.WhiteSmoke;
            pnlSearchScope.Controls.Add(rdoObsidian);
            pnlSearchScope.Controls.Add(rdoRPOL);
            pnlSearchScope.Controls.Add(rdoSearchDefault);
            pnlSearchScope.Location = new Point(396, 87);
            pnlSearchScope.Name = "pnlSearchScope";
            pnlSearchScope.Size = new Size(425, 42);
            pnlSearchScope.TabIndex = 4;
            pnlSearchScope.Visible = false;
            pnlSearchScope.Paint += PnlSearchScope_Paint;
            pnlSearchScope.Resize += PnlSearchScope_Resize;
            // 
            // rdoObsidian
            // 
            rdoObsidian.AutoSize = true;
            rdoObsidian.Location = new Point(272, 12);
            rdoObsidian.Name = "rdoObsidian";
            rdoObsidian.Size = new Size(160, 19);
            rdoObsidian.TabIndex = 2;
            rdoObsidian.TabStop = true;
            rdoObsidian.Text = "Search only Obsidian wiki";
            rdoObsidian.UseVisualStyleBackColor = true;
            // 
            // rdoRPOL
            // 
            rdoRPOL.AutoSize = true;
            rdoRPOL.Location = new Point(154, 12);
            rdoRPOL.Name = "rdoRPOL";
            rdoRPOL.Size = new Size(118, 19);
            rdoRPOL.TabIndex = 1;
            rdoRPOL.TabStop = true;
            rdoRPOL.Text = "Search only RPOL";
            rdoRPOL.UseVisualStyleBackColor = true;
            // 
            // rdoSearchDefault
            // 
            rdoSearchDefault.AutoSize = true;
            rdoSearchDefault.Checked = true;
            rdoSearchDefault.Location = new Point(13, 12);
            rdoSearchDefault.Name = "rdoSearchDefault";
            rdoSearchDefault.Size = new Size(107, 19);
            rdoSearchDefault.TabIndex = 0;
            rdoSearchDefault.TabStop = true;
            rdoSearchDefault.Text = "RPOL & Obsidian";
            rdoSearchDefault.UseVisualStyleBackColor = true;
            // 
            // pnlSearchResults
            // 
            pnlSearchResults.BackColor = Color.WhiteSmoke;
            pnlSearchResults.BorderStyle = BorderStyle.FixedSingle;
            pnlSearchResults.Controls.Add(lstSearchResults);
            pnlSearchResults.Location = new Point(338, 146);
            pnlSearchResults.Name = "pnlSearchResults";
            pnlSearchResults.Size = new Size(746, 552);
            pnlSearchResults.TabIndex = 5;
            pnlSearchResults.Visible = false;
            // 
            // lstSearchResults
            // 
            lstSearchResults.BackColor = SystemColors.Control;
            lstSearchResults.ColumnWidth = 200;
            lstSearchResults.Dock = DockStyle.Fill;
            lstSearchResults.Font = new Font("Segoe UI", 11F);
            lstSearchResults.FormattingEnabled = true;
            lstSearchResults.IntegralHeight = false;
            lstSearchResults.Location = new Point(0, 0);
            lstSearchResults.MultiColumn = true;
            lstSearchResults.Name = "lstSearchResults";
            lstSearchResults.Size = new Size(744, 550);
            lstSearchResults.TabIndex = 0;
            lstSearchResults.Visible = false;
            lstSearchResults.MouseClick += LstSearchResults_MouseClick;
            // 
            // btnSearch
            // 
            btnSearch.Enabled = false;
            btnSearch.Font = new Font("Segoe UI", 12F);
            btnSearch.Location = new Point(737, 30);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(90, 31);
            btnSearch.TabIndex = 2;
            btnSearch.Text = "Search";
            btnSearch.UseVisualStyleBackColor = true;
            btnSearch.Click += BtnSearch_Click;
            // 
            // lblSearchCharacterCnt
            // 
            lblSearchCharacterCnt.AutoSize = true;
            lblSearchCharacterCnt.Font = new Font("Segoe UI", 9F);
            lblSearchCharacterCnt.Location = new Point(396, 64);
            lblSearchCharacterCnt.Name = "lblSearchCharacterCnt";
            lblSearchCharacterCnt.Size = new Size(118, 15);
            lblSearchCharacterCnt.TabIndex = 3;
            lblSearchCharacterCnt.Text = "Characters entered: 0";
            lblSearchCharacterCnt.Visible = false;
            // 
            // txtSearch
            // 
            txtSearch.Font = new Font("Segoe UI", 10F);
            txtSearch.Location = new Point(396, 31);
            txtSearch.MaxLength = 60;
            txtSearch.Name = "txtSearch";
            txtSearch.Size = new Size(425, 25);
            txtSearch.TabIndex = 1;
            txtSearch.TextChanged += TxtSearch_TextChanged;
            txtSearch.KeyDown += TxtSearch_KeyDown;
            txtSearch.KeyPress += TxtSearch_KeyPress;
            txtSearch.EnterPressed += TxtSearch_EnterPressed;
            // 
            // lblSearchPrompt
            // 
            lblSearchPrompt.AutoSize = true;
            lblSearchPrompt.Font = new Font("Segoe UI", 12F);
            lblSearchPrompt.Location = new Point(198, 34);
            lblSearchPrompt.Name = "lblSearchPrompt";
            lblSearchPrompt.Size = new Size(152, 21);
            lblSearchPrompt.TabIndex = 0;
            lblSearchPrompt.Text = "Enter search term(s):";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1024, 768);
            Controls.Add(pnlSearch);
            Controls.Add(statusStrip);
            Controls.Add(menuStrip);
            MainMenuStrip = menuStrip;
            Name = "Form1";
            StartPosition = FormStartPosition.Manual;
            Text = "Player Assistant";
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            pnlSearch.ResumeLayout(false);
            pnlSearch.PerformLayout();
            pnlSearchScope.ResumeLayout(false);
            pnlSearchScope.PerformLayout();
            pnlSearchResults.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem showToolStripMenuItem;
        private ToolStripMenuItem searchToolStripMenuItem;
        private ToolStripMenuItem loginInfoToolStripMenuItem;
        private ToolStripMenuItem showPostTotalsToolStripMenuItem;
        private ToolStripMenuItem showDiceRollsToolStripMenuItem;
        private ToolStripMenuItem xpToolStripMenuItem;
        private ToolStripMenuItem partyToolStripMenuItem;
        private ToolStripMenuItem formerPcsToolStripMenuItem;
        private ToolStripMenuItem myHeroBriefingToolStripMenuItem;
        private ToolStripMenuItem adventureOutlineToolStripMenuItem;
        private ToolStripMenuItem regionalMapToolStripMenuItem;
        private ToolStripMenuItem translatorToolStripMenuItem;
        private ToolStripMenuItem orcishTranslatorToolStripMenuItem;
        private ToolStripMenuItem elvenTranslatorToolStripMenuItem;
        private ToolStripMenuItem ghukliakTranslatorToolStripMenuItem;
        private ToolStripMenuItem exitToolStripMenuItem;
        private ToolStripMenuItem settingsToolStripMenuItem;
        private ToolStripMenuItem skipHeroImageParadeAtStartupToolStripMenuItem;
        private ToolStripMenuItem whiteMarbleBackgroundTilingToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem authorToolStripMenuItem;
        private ToolStripMenuItem checkForUpdateToolStripMenuItem;
        private ToolStripMenuItem versionToolStripMenuItem;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusToolStripStatusLabel;
        private ToolStripStatusLabel statusActivityToolStripStatusLabel;
        private Panel pnlSearch;
        private Button btnSearch;
        private Panel pnlSearchResults;
        private Panel pnlSearchScope;
        private ListBox lstSearchResults;
        private RadioButton rdoObsidian;
        private RadioButton rdoRPOL;
        private RadioButton rdoSearchDefault;
        private Label lblSearchCharacterCnt;
        private SearchTextBox txtSearch;
        private Label lblSearchPrompt;
    }
}

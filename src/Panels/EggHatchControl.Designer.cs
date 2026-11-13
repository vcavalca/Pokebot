namespace Pokebot.Panels
{
    partial class EggHatchControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this._helpLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // _helpLabel
            //
            this._helpLabel.Location = new System.Drawing.Point(12, 12);
            this._helpLabel.Name = "_helpLabel";
            this._helpLabel.Size = new System.Drawing.Size(726, 256);
            this._helpLabel.TabIndex = 0;
            //
            // EggHatchControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._helpLabel);
            this.Name = "EggHatchControl";
            this.Size = new System.Drawing.Size(750, 280);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label _helpLabel;
    }
}

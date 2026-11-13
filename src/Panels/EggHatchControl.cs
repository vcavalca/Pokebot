using System.Windows.Forms;

namespace Pokebot.Panels
{
    /// <summary>
    /// Egg Hatch has no options: the egg is already decided when it is received, hatching can't change it.
    /// </summary>
    public partial class EggHatchControl : UserControl
    {
        public EggHatchControl()
            : this(Messages.BotEggHatch_Help)
        {
        }

        /// <summary>Same panel (instructions only) for the other bots without options (Day-Care Egg).</summary>
        public EggHatchControl(string help)
        {
            InitializeComponent();
            _helpLabel.Text = help;
        }
    }
}

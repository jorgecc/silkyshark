using System.Windows.Forms;

namespace Silky_Shark
{
    public partial class About : Form
    {
        public About()
        {
            InitializeComponent();
        }

        // To the GitHub page
        private void linkLabel_toGit_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/stoicshark/silkyshark");
        }
    }
}

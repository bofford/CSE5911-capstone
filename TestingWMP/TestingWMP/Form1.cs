using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestingWMP
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            axWindowsMediaPlayer1.URL = @"C:\The_Black_Eyed_Peas.mp3";
            System.Console.Write("Initial volume: {0}", axWindowsMediaPlayer1.settings.volume);
            axWindowsMediaPlayer1.settings.volume = 100;
            System.Console.Write("Final volume: {0}", axWindowsMediaPlayer1.settings.volume);

        }
    }
}

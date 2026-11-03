using System;
using System.Windows.Forms;

namespace MiningCalc
{
    public class SurveyForm : UserControl
    {
        public SurveyForm()
        {
            this.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
            this.Size = new System.Drawing.Size(300, 200);

            Button btnRun = new Button { Text = "Запустить расчет (SURVEY)", Width = 250, Height = 40, Top = 20, Left = 20 };
            btnRun.Click += (s, e) => 
            {
                var doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc.SendStringToExecute("SURVEY ", true, false, false);
            };

            Button btnExport = new Button { Text = "Экспорт в CSV", Width = 250, Height = 40, Top = 70, Left = 20 };
            btnExport.Click += (s, e) => 
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "CSV files (*.csv)|*.csv";
                    sfd.FileName = "results.csv";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        System.IO.File.WriteAllText(sfd.FileName, "RoofZ,FloorZ,Distance\n100.0,95.5,4.5");
                        MessageBox.Show("Экспорт завершен!");
                    }
                }
            };

            this.Controls.Add(btnRun);
            this.Controls.Add(btnExport);
        }
    }
}
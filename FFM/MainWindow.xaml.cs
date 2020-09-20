using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Serialization;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FFM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static List<RadioCommand> radioCommands;

        private Process process;

        private string commandsFile = "commands.xml";
        private string executable = "ffmpeg.exe";

        private string duration = "";
        private double duractionSeconds = 0;

        public MainWindow()
        {
            InitializeComponent();

            XmlSerializer serializer = new XmlSerializer(typeof(List<RadioCommand>));

            if (!File.Exists(commandsFile))
            {
                serializer.Serialize(File.OpenWrite(commandsFile), new List<RadioCommand>()
                { new RadioCommand { Command = " EXAMPLE (beginning space is required)", Name = "EXAMPLE", OutExt = ".EXMAPLE (dot is required)" } });

                MessageBox.Show("There are no commands. \r\n Checkout commands.xml file.", "Error");

                Environment.Exit(0);
            }

            if (!File.Exists(executable))
            {
                MessageBox.Show("ffmpeg.exe is missing. \r\n Checkout ffmpeg.org", "Error");
                Environment.Exit(0);
            }


            radioCommands = (List<RadioCommand>)serializer.Deserialize(File.OpenRead(commandsFile));

            radioCommands.Select(x => x.Name).ToList().ForEach(i => RadioPanel.Children.Add(new RadioButton() { Content = i, Width = 170 }));
            RadioPanel.Children.OfType<RadioButton>().First().IsChecked = true;

            FFMPEG_version();
        }

        private async void FFMPEG_version()
        {
            string vcode = await Task.Run(() =>
            {
                var pi = new ProcessStartInfo(executable, "-version");
                pi.WindowStyle = ProcessWindowStyle.Hidden;
                pi.UseShellExecute = false;
                pi.RedirectStandardOutput = true;
                pi.CreateNoWindow = true;
                var p = new Process();
                p.StartInfo = pi;
                p.Start();
                var stdout = p.StandardOutput.ReadToEnd().Split(" ");
                var versionCode = "ffmpeg version: " + (stdout.Length > 2 ? stdout[2] : "unknown");
                p.WaitForExit();

                return versionCode;
            });

            ffmpegVersionBlock.Text = vcode;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            string[] droppedFiles = null;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                droppedFiles = e.Data.GetData(DataFormats.FileDrop, true) as string[];
            }

            if ((null == droppedFiles) || (!droppedFiles.Any())) { return; }

            string selectedRd = (string)RadioPanel.Children.OfType<RadioButton>().FirstOrDefault(x => (bool)x.IsChecked).Content;

            RadioCommand RdC = radioCommands.Where(x => x.Name == selectedRd).First();


            string command = RdC.Command;

            string ext = RdC.OutExt[0] == '.' ? RdC.OutExt : "." + RdC.OutExt;

            string mainFile = droppedFiles.FirstOrDefault(x => x.Substring(x.Length - 3) == "mkv" || x.Substring(x.Length - 3) == "mp4" || x.Substring(x.Length - 3) == "avi" || x.Substring(x.Length - 3) == "ts" || x.Substring(x.Length - 3) == "mov" || x.Substring(x.Length - 3) == "wmv");

            if (mainFile == null)
                return;

            string suffix = (bool)ffmCheckbox.IsChecked ? "" : "_[FFM]";

            string outputFile = mainFile.Remove(mainFile.Length - 4) + suffix + ext;

            int i = 0;
            while (File.Exists(outputFile))
            {
                i++;
                outputFile = mainFile.Remove(mainFile.Length - 4) + suffix + $"[{i}]{ext}";
            }

            string trim = (ssTextBox.Text.Trim() != "" ? " -ss " + ssTextBox.Text : "")
                + (toTextBox.Text.Trim() != "" ? " -to " + toTextBox.Text : "");

            string crop = (bool)oneoneHCropCheckBox.IsChecked ? "-vf \"crop=ih:ih\"" : 
                (
                    (bool)oneoneWCropCheckBox.IsChecked ? "-vf \"crop=iw:iw\"" : ""
                );


            string concatenated = " -y " + trim + string.Join(" ", droppedFiles.Select(s => $" -i \"{s}\" ").ToArray()) +
                (
                ext != ".mkv" ? "" :
                string.Join(" ", Enumerable.Repeat(" -map ", droppedFiles.Length).Select((s, i) => s + i.ToString()))
                )
                + $" {command} {crop} \"{outputFile}\"";

            cancelButton.Visibility = Visibility.Visible;

            process = new Process()
            {
                StartInfo =
                {
                    FileName = executable,
                    Arguments = concatenated,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = false
                },
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += Process_OutputDataReceived;

            process.Exited += (object sender, EventArgs e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    cancelButton.Visibility = Visibility.Hidden;

                    ffmpegProgessBlock.Text = "Idle";

                    if ((bool)delCheckBox.IsChecked)
                        droppedFiles.ToList().ForEach(f => System.IO.File.Delete(f));

                    if ((bool)exitCheckBox.IsChecked)
                        Environment.Exit(1);
                });

            };

            process.Start();
            process.BeginErrorReadLine();
        }


        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                // Calculate Duration by given input data

                //if (!string.IsNullOrEmpty(toTextBox.Text))
                //{
                //    var to = Convert.ToDateTime(toTextBox.Text);

                //    if (!string.IsNullOrEmpty(ssTextBox.Text))
                //    {
                //        to.Subtract(Convert.ToDateTime(ssTextBox.Text));
                //    }

                //    duration = to.ToString("HH:mm:ss.ff");
                //    duractionSeconds = (to.Hour * 3600) + (to.Minute * 60) + to.Second + to.Millisecond;
                //}
                //else if (!string.IsNullOrEmpty(ssTextBox.Text))
                //{

                //}
                //else
                //{
                var m = Regex.Match(e.Data, "Duration: (.*?), start:");
                if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                {
                    duration = m.Groups[1].Value;
                    var sp = duration.Split(':');
                    duractionSeconds = ((Double.Parse(sp[0]) * 3600) + (Double.Parse(sp[1]) * 60) + (Double.Parse(sp[2])));
                }
                //}

                var m2 = Regex.Match(e.Data, @"time=(.*?)\s");

                if (!string.IsNullOrEmpty(m2.Groups[1].Value))
                {
                    var sp = m2.Groups[1].Value.Split(':');

                    if (sp.Length > 2)
                    {
                        double progSecs = ((Double.Parse(sp[0]) * 3600) + (Double.Parse(sp[1]) * 60) + (Double.Parse(sp[2])));
                        int percentage = (int)Math.Floor((progSecs / duractionSeconds) * 100);
                        Dispatcher.Invoke(() => { ffmpegProgessBlock.Text = $"{percentage}% - {m2.Groups[1]}/{duration}"; });
                    }
                }
            }
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            new About().ShowDialog();
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            process?.Kill();
            ffmpegProgessBlock.Text = "Idle";
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            process?.Kill();
        }
    }

    public class RadioCommand
    {
        public string Name { get; set; }
        public string OutExt { get; set; }
        public string Command { get; set; }
    }
}

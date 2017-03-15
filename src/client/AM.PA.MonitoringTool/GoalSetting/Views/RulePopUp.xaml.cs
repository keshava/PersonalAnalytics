﻿using GoalSetting.Rules;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using Shared;

namespace GoalSetting.Views
{
    /// <summary>
    /// Interaction logic for RulePopUp.xaml
    /// </summary>
    public partial class RulePopUp : Window
    {
        private ObservableCollection<PARule> rules;

        public RulePopUp(ObservableCollection<PARule> rules)
        {
            InitializeComponent();
            this.rules = rules;
            AddHeaderPictures();
            AddRules();
        }

        internal void AddHeaderPictures()
        {
            Close.Source = BitmapToImageSource(Properties.Resources.close);
            Dashboard.Source = BitmapToImageSource(Properties.Resources.dashboard);
        }

        internal void AddRules()
        {
            foreach (PARule rule in rules)
            {
                StackPanel container = new StackPanel();
                container.Tag = rule;
                container.Background = new SolidColorBrush(Colors.LightGray);
                container.Orientation = System.Windows.Controls.Orientation.Horizontal;
                Thickness containerMargin = container.Margin;
                container.Height = 30;
                containerMargin.Bottom = 10;
                container.Margin = containerMargin;

                System.Windows.Controls.Image smiley = new System.Windows.Controls.Image();
                smiley.Height = 25;

                switch (rule.Progress.Status)
                {
                    case ProgressStatus.VeryLow:
                        smiley.Source = BitmapToImageSource(Properties.Resources.smiley_5);
                        break;
                    case ProgressStatus.Low:
                        smiley.Source = BitmapToImageSource(Properties.Resources.smiley_4);
                        break;
                    case ProgressStatus.Average:
                        smiley.Source = BitmapToImageSource(Properties.Resources.smiley_3);
                        break;
                    case ProgressStatus.High:
                        smiley.Source = BitmapToImageSource(Properties.Resources.smiley_2);
                        break;
                    case ProgressStatus.VeryHigh:
                        smiley.Source = BitmapToImageSource(Properties.Resources.smiley_1);
                        break;
                }

                TextBlock text = new TextBlock();
                text.VerticalAlignment = VerticalAlignment.Center;
                text.Inlines.Add(rule.ToString());
                text.Inlines.Add(new LineBreak());
                text.Inlines.Add(rule.GetProgressMessage());

                Thickness margin = text.Margin;
                margin.Left = 20;
                text.Margin = margin;

                container.Children.Add(smiley);
                container.Children.Add(text);
                container.MouseLeftButtonDown += Rule_MouseLeftButtonDown;

                Rules.Children.Add(container);
            }

        }

        private void Rule_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PARule rule = (PARule) (sender as FrameworkElement).Tag;
            if (rule.TimeSpan == RuleTimeSpan.Week || rule.TimeSpan == RuleTimeSpan.Month)
            {
                GoalSettingManager.Instance.OpenRetrospection(VisType.Week);
            }
            else
            {
                GoalSettingManager.Instance.OpenRetrospection(VisType.Day);
            }
        }

        private BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        public new bool? ShowDialog()
        {
            Screen mainScreen = Screen.AllScreens[0];

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.Size.Height >= mainScreen.WorkingArea.Size.Height && screen.WorkingArea.Width >= mainScreen.WorkingArea.Width)
                {
                    mainScreen = screen;
                }
            }

            this.Left = mainScreen.WorkingArea.Right - this.Width;
            this.Top = mainScreen.WorkingArea.Bottom - this.Height;

            return base.ShowDialog();
        }

        private void Close_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.Close();
            GoalSettingManager.Instance.DeleteCachedResults();
        }

        private void Dashboard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.Close();
            GoalSettingManager.Instance.OpenRetrospection(VisType.Week);
        }
    }

}
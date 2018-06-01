using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Models.DTO;
using Tweetinvi.Models.Entities;

namespace TwitterPicFrame
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool TwitterAuthenticated = false;
        private bool MongoDBAuthenticated = false;

        public BitmapImage _ImageFromTweet;
        public BitmapImage ImageFromTweet { get { return _ImageFromTweet; } set { _ImageFromTweet = value; OnPropertyChanged("ImageFromTweet"); } }

        public String _TextURL;
        public String TextURL { get { return _TextURL; } set { _TextURL = value; OnPropertyChanged("TextURL"); } }

        Random random = new Random();
        Tweetinvi.Streaming.IFilteredStream Filteredstream = null;
        System.Timers.Timer timer = new System.Timers.Timer();

        public MainWindow()
        {
            DataContext = this;
            InitializeComponent();

            TwitterAuthenticated = LoginTwitter();
            MongoDBAuthenticated = LoginMongoDB();

            StartStreaming();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (Filteredstream != null)
                Filteredstream.StopStream();
        }

        private bool LoginMongoDB()
        {
            return false;
        }

        private bool LoginTwitter()
        {
            //Auth.SetApplicationOnlyCredentials(Settings.GetValueFromConfig("Twitter_CONSUMERKEY"), Settings.GetValueFromConfig("Twitter_CONSUMERSECRET"));
            Auth.SetUserCredentials(Settings.GetValueFromConfig("Twitter_CONSUMERKEY"), Settings.GetValueFromConfig("Twitter_CONSUMERSECRET"),
                Settings.GetValueFromConfig("Twitter_ACCESSTOKEN"), Settings.GetValueFromConfig("Twitter_ACCESSTOKENSECRET"));
            return true;
        }

        private void StartStreaming()
        {
            if (!TwitterAuthenticated)
                return;

            List<String> FilterStrings = Settings.GetValueFromConfig("Stream_FilterTrack").Split(',').ToList();

            Filteredstream = Stream.CreateFilteredStream();

            foreach (String filter in FilterStrings)
                Filteredstream.AddTrack(filter);

            Console.WriteLine("Filter tracks: " + String.Join("||", FilterStrings));

            Filteredstream.MatchingTweetReceived += (sender2, args) =>
            {
                ITweet tw = args.Tweet;

                Console.WriteLine("A tweet containing has been found");

                if (tw.RetweetedTweet != null)
                {
                    tw = tw.RetweetedTweet;
                    Console.WriteLine("Extracting main Tweet rather than RT");
                }

                Console.WriteLine("The tweet is " + tw.FullText);

                TextURL = tw.Url;

                List<IMediaEntity> Medias = tw.Media;
                if (Medias != null && Medias.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke((Action)delegate
                    {
                        IMediaEntity m = Medias[random.Next(Medias.Count)];
                        if (m.MediaURL.Contains(".jpg"))
                            ImageFromTweet = new BitmapImage(new Uri(m.MediaURL));
                    });
                }
            };

            Filteredstream.StreamStarted += (sender2, args) =>
            {
                Console.WriteLine("Stream started");
            };

            Filteredstream.StreamStopped += (sender2, args) =>
            {
                Console.WriteLine(args.Exception);
                Console.WriteLine(args.DisconnectMessage);

                timer.Stop();
                timer.Enabled = false;
            };

            timer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            timer.Interval = 10000;
            timer.Enabled = true;

            Task T = Filteredstream.StartStreamMatchingAnyConditionAsync();
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            if (Filteredstream != null)
                Console.WriteLine(Filteredstream.StreamState);
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
    

}

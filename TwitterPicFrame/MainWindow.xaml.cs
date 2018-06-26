using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Driver;
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

        MongoClient client;
        IMongoDatabase database;
        IMongoCollection<TweetInfo> TweetCollection;

        private BitmapImage _ImageFromTweet;
        public BitmapImage ImageFromTweet { get { return _ImageFromTweet; } set { _ImageFromTweet = value; OnPropertyChanged("ImageFromTweet"); } }

        private String _TextURL;
        public String TextURL { get { return _TextURL; } set { _TextURL = value; OnPropertyChanged("TextURL"); } }

        private bool _IsShowImage;
        public bool IsShowImage { get { return _IsShowImage; } set { _IsShowImage = value; OnPropertyChanged("IsShowImage"); } }


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

        private void Reset()
        {
            IsShowImage = false;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (Filteredstream != null)
                Filteredstream.StopStream();
        }

        private bool LoginMongoDB()
        {
            try
            {
                MongoClient client = new MongoClient(Settings.GetValueFromConfig("MongoDB_Address"));
                database = client.GetDatabase(Settings.GetValueFromConfig("MongoDB_Database"));

                if (client == null)
                    return false;
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
            return true;
        }

        private bool LoginTwitter()
        {
            //TODO Exception handling

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
                ProcessTweet(tw);
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

        private void ProcessTweet(ITweet tw)
        {
            Console.WriteLine("A tweet has been found");

            if (tw.RetweetedTweet != null)
            {
                tw = tw.RetweetedTweet;
                Console.WriteLine("Extracting main Tweet rather than RT");
            }

            Console.WriteLine("The tweet is " + tw.FullText);

            List<IMediaEntity> Medias = tw.Media;
            if (Medias != null && Medias.Count > 0)
            {
                Application.Current.Dispatcher.Invoke(delegate
                {
                    IMediaEntity m = Medias[random.Next(Medias.Count)];
                    if (m.MediaURL.Contains(".jpg"))
                    {
                        TextURL = tw.Url;
                        if(IsShowImage)
                            ImageFromTweet = new BitmapImage(new Uri(m.MediaURL));
                    }
                });

                SaveNewTweetToMongo(tw);
            }
        }

        private void SaveNewTweetToMongo(ITweet tw)
        {
            if (!MongoDBAuthenticated)
                return;

            TweetCollection = database.GetCollection<TweetInfo>(Settings.GetValueFromConfig("MongoDB_Collection"));

            if (!isTweetSaved(tw.IdStr))
            {
                TweetInfo ti = new TweetInfo();
                ti.ID = tw.IdStr;
                ti.Name = tw.CreatedBy.Name;
                ti.ScreenName = tw.CreatedBy.ScreenName;
                ti.MainTweet = tw;

                TweetCollection.InsertOne(ti);
            }
        }

        private bool isTweetSaved(String id)
        {
            IMongoCollection<BsonDocument> BsonCollection = database.GetCollection<BsonDocument>(Settings.GetValueFromConfig("MongoDB_Collection"));
            BsonDocument filter = new BsonDocument(new BsonElement("_id", id));

            if (BsonCollection.Find(filter).Count() > 0)
                return true;
            else
                return false;
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
    
    [BsonIgnoreExtraElements]
    public class TweetInfo
    {
        [BsonId]
        public String ID { get; set; }
        [BsonElement("name")]
        public String Name { get; set; }
        [BsonElement("screen_name")]
        public String ScreenName { get; set; }

        [BsonElement("raw_tweet")]
        public String MainTweetJson {
            get {
                return MainTweet.ToJson();
            }
            set {
                MainTweet = value.ConvertJsonTo<ITweet>();
            }
        }

        [BsonIgnore]
        public ITweet MainTweet { get; set; }
    }
}

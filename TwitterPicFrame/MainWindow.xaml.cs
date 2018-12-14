using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
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

        private string CurrentImageUrl = "";

        Random random = new Random();
        Tweetinvi.Streaming.IFilteredStream Filteredstream = null;
        System.Timers.Timer timer = new System.Timers.Timer();

        public MainWindow()
        {
            DataContext = this;
            InitializeComponent();

            TwitterAuthenticated = LoginTwitter();
            MongoDBAuthenticated = LoginMongoDB();

            Reset();
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
            Auth.SetUserCredentials(
                Settings.GetValueFromConfig("Twitter_CONSUMERKEY"), Settings.GetValueFromConfig("Twitter_CONSUMERSECRET"),
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

                timer.Enabled = true;
                Task.Factory.StartNew(() => { GCS_SaveInNewThread(); });
            };

            Filteredstream.StreamStopped += (sender2, args) =>
            {
                Console.WriteLine(args.Exception);
                Console.WriteLine(args.DisconnectMessage);

                timer.Stop();
                timer.Enabled = false;

                Console.WriteLine("Restarting in 5 Second");
                Thread.Sleep(5000);
                RestartStream();
            };

            timer.Elapsed += new ElapsedEventHandler(Timer_Elapsed);
            timer.Interval = 10000;

            RestartStream();
        }

        private void RestartStream()
        {
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
                        CurrentImageUrl = m.MediaURL;
                        if (IsShowImage)
                            ImageFromTweet = new BitmapImage(new Uri(CurrentImageUrl));
                    }
                });

                if (!Mongo_isTweetSaved(tw.IdStr))
                {
                    Mongo_SaveNewTweet(tw);
                    RecordQueue.Enqueue(tw);
                    //GCS_SaveNewTweetImage(tw);
                }
            }
        }

        private bool Mongo_isTweetSaved(String id)
        {
            IMongoCollection<BsonDocument> BsonCollection = database.GetCollection<BsonDocument>(Settings.GetValueFromConfig("MongoDB_Collection"));
            BsonDocument filter = new BsonDocument(new BsonElement("_id", id));

            if (BsonCollection.Find(filter).Count() > 0)
                return true;
            else
                return false;
        }

        private void Mongo_SaveNewTweet(ITweet tw)
        {
            if (!MongoDBAuthenticated)
                return;

            TweetInfo ti = new TweetInfo();
            ti.ID = tw.IdStr;
            ti.Name = tw.CreatedBy.Name;
            ti.ScreenName = tw.CreatedBy.ScreenName;
            ti.MainTweet = tw;

            TweetCollection = database.GetCollection<TweetInfo>(Settings.GetValueFromConfig("MongoDB_Collection"));
            TweetCollection.InsertOne(ti);

            Console.WriteLine("Tweet Saved into MongoDB");
        }

        Queue<ITweet> RecordQueue = new Queue<ITweet>();
        StorageClient storageClient = null;

        private void GCS_SaveInNewThread()
        {
            String ProjectId = Settings.GetValueFromConfig("GCS_projectId");
            String BucketName = Settings.GetValueFromConfig("GCS_bucketName");
            String BaseFolderName = Settings.GetValueFromConfig("GCS_baseFolderName");

            if (storageClient == null)
            {
                string CredPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + Settings.GetValueFromConfig("GCS_jsonKey");

                var credential = GoogleCredential.FromFile(CredPath);
                storageClient = StorageClient.Create(credential);
            }

            System.IO.Stream fs = new System.IO.MemoryStream(0);
            String PathName = BaseFolderName += @"/";

            var checkObj1 = storageClient.ListObjects(BucketName, PathName);
            if (checkObj1.Count() <= 0)
            {
                var result = storageClient.UploadObject(BucketName, PathName, null, fs);
                Console.WriteLine($"Image {result.Name} Uploaded to GCS");
            }

            while (true)
            {
                if (RecordQueue.Count == 0)
                {
                    Thread.Sleep(2000);
                    continue;
                }

                ITweet tw = RecordQueue.Dequeue();

                string TweetFolderName = tw.CreatedBy.ScreenName;

                var ffs = new System.IO.MemoryStream(0);
                String TweetPathName = PathName + TweetFolderName + @"/";
                var checkObj2 = storageClient.ListObjects(BucketName, TweetPathName);
                if (checkObj2.Count() <= 0)
                {
                    var result = storageClient.UploadObject(BucketName, TweetPathName, null, ffs);
                    Console.WriteLine($"Image {result.Name} Uploaded to GCS");
                }

                foreach (IMediaEntity m in tw.Media)
                {
                    if (m.MediaURL.Contains(".jpg"))
                    {
                        String ImageName = m.IdStr + ".jpg";
                        String finalFilePathName = TweetPathName + ImageName;
                        //var ImageFromTweet = new BitmapImage(new Uri(m.MediaURL));

                        System.Net.WebClient client = new System.Net.WebClient();
                        fs = new System.IO.MemoryStream(client.DownloadData(m.MediaURL));
                        //Console.WriteLine(PathNameUntili);

                        var result = storageClient.UploadObject(BucketName, finalFilePathName, null, ffs);
                        Console.WriteLine($"Image {result.Name} Uploaded to GCS");
                    }
                }
            }
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Filteredstream != null)
                Console.WriteLine(Filteredstream.StreamState);
        }

        //Show/hide the image from current tweet
        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (IsShowImage)
                ImageFromTweet = new BitmapImage(new Uri(CurrentImageUrl));
            else
                ImageFromTweet = null;
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

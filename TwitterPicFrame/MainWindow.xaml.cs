using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace TwitterPicFrame
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool TwitterAuthenticated = false;
        private bool MongoDBAuthenticated = false;

        public MainWindow()
        {
            InitializeComponent();

            TwitterAuthenticated = LoginTwitter();
            MongoDBAuthenticated = LoginMongoDB();
        }

        private bool LoginMongoDB()
        {
            return false;
        }

        private bool LoginTwitter()
        {
            Auth.SetApplicationOnlyCredentials(Settings.GetValueFromConfig("Twitter_CONSUMERKEY"), Settings.GetValueFromConfig("Twitter_CONSUMERSECRET"));
            //Auth.SetUserCredentials(CONSUMER_KEY, CONSUMER_SECRET, ACCESS_TOKEN, ACCESS_TOKEN_SECRET);
            return true;
        }
    }
}

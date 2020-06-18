using Bili;
using BiliLive;
using BiliLogin;
using JsonUtil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
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

namespace LatiaoCollector
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();

            if (File.Exists("login.dat"))
            {
                using(FileStream fileStream = new FileStream("login.dat",FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    object obj = binaryFormatter.Deserialize(fileStream);

                    CookieCollection cookies = (CookieCollection)obj;
                    BiliApi.LoginCookies = cookies;
                    UserInfo userInfo = UserInfo.GetUserInfo(cookies);
                    Dispatcher.Invoke(() =>
                    {
                        UserInfoBox.Text = userInfo.Uname;
                    });
                }
            }
            
        }

        private abstract class RoomInfo
        {
            public abstract int RoomId { get; set; }

            public abstract override string ToString();
        }

        private class HomepageRoomInfo : RoomInfo
        {
            public int UId { get; set; }
            public string UName { get; set; }
            
            public override int RoomId { get; set; }
            public string Title { get; set; }

            public HomepageRoomInfo(Json.Value value)
            {
                UId = value["uid"];
                UName = value["uname"];
                RoomId = value["roomid"];
                Title = value["title"];
            }

            public override string ToString()
            {
                return $"Uid >> {UId}, UName >> {UName}, RoomId >> {RoomId}, Title >> {Title}";
            }
        }

        private class TopListRoomInfo : RoomInfo
        {
            public int UId { get; set; }
            public string UName { get; set; }

            public override int RoomId { get; set; }

            public TopListRoomInfo(Json.Value value)
            {
                UId = value["uid"];
                UName = value["uname"];
                string link = value["link"];
                RoomId = int.Parse(link.Substring(1));
            }

            public override string ToString()
            {
                return $"Uid >> {UId}, UName >> {UName}, RoomId >> {RoomId}";
            }
        }

        private List<HomepageRoomInfo> GetHomepageLotteryRooms()
        {
            Json.Value list = BiliApi.GetJsonResult("https://api.live.bilibili.com/xlive/web-interface/v1/index/getList", new Dictionary<string, string> { { "platform", "web" } }, false);

            List<HomepageRoomInfo> lotteryRooms = new List<HomepageRoomInfo>();

            if (list["code"] == 0)
            {
                Json.Value areaList = list["data"]["room_list"];
                foreach (Json.Value area in areaList)
                {
                    Json.Value roomList = area["list"];
                    foreach (Json.Value room in roomList)
                    {
                        Json.Value pendantInfo = room["pendant_Info"];
                        foreach (KeyValuePair<string, Json.Value> tag in pendantInfo)
                        {
                            //if (tag["name"] == "lottery_draw_2")
                            if (tag.Value["text"] == "正在抽奖")
                            {
                                lotteryRooms.Add(new HomepageRoomInfo(room));
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                AppendLog($"error >> {list["message"]}");
            }

            return lotteryRooms;
        }

        private int[] GetAreaIds()
        {
            Json.Value list = BiliApi.GetJsonResult("https://api.live.bilibili.com/room/v1/Area/getList", null, false);
            if (list["code"] == 0)
            {
                List<int> ids = new List<int>();
                foreach (Json.Value area in list["data"])
                {
                    ids.Add(area["id"]);
                }
                return ids.ToArray();
            }
            else
            {
                AppendLog($"error >> {list["message"]}");
                return new int[] { 0 };
            }
        }

        private List<TopListRoomInfo> GetHourTopRooms(int areaId)
        {
            Json.Value tops = BiliApi.GetJsonResult("https://api.live.bilibili.com/rankdb/v1/Rank2018/getTop", new Dictionary<string, string> { { "type", "master_realtime_hour" }, { "type_id", "areaid_realtime_hour" }, { "area_id", areaId.ToString() } }, false);

            List<TopListRoomInfo> topRooms = new List<TopListRoomInfo>();

            if (tops["code"] == 0)
            {
                Json.Value list = tops["data"]["list"];
                foreach (Json.Value room in list)
                {
                    topRooms.Add(new TopListRoomInfo(room));
                }
            }
            else
            {
                AppendLog($"error >> {tops["message"]}");
            }

            return topRooms;
        }

        private Json.Value GetRoomLotteries(int roomId)
        {
            Json.Value lottery = BiliApi.GetJsonResult("https://api.live.bilibili.com/xlive/lottery-interface/v1/lottery/Check", new Dictionary<string, string> { { "roomid", roomId.ToString() } }, false);
            return lottery;
        }

        private void SendPkLotteryRequest(int roomId, int lotteryId)
        {
            string csrf_token = null;
            foreach (Cookie cookie in BiliApi.LoginCookies)
            {
                if (cookie.Name == "bili_jct")
                {
                    csrf_token = cookie.Value;
                    break;
                }
            }

            string url = "https://api.live.bilibili.com/xlive/lottery-interface/v2/pk/join";
            string postContent = $"id={lotteryId}&roomid={roomId}&type=pk&csrf_token={csrf_token}&csrf={csrf_token}&visit_id=";
            byte[] postData = Encoding.ASCII.GetBytes(postContent);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(BiliApi.LoginCookies);
            request.Referer = $"https://live.bilibili.com/{roomId}";

            using (Stream stream = request.GetRequestStream())
                stream.Write(postData, 0, postData.Length);

            string result = null;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
                result = reader.ReadToEnd();

            Json.Value join = Json.Parser.Parse(result);
            if (join["code"] == 0)
            {
                AppendLog($"{join["data"]["award_name"]} x{join["data"]["award_num"]} (Pk in Room >> {roomId})");
            }
            else
            {
                AppendLog($"error >> {join["message"]}");
            }
        }

        private void SendGuardLotteryRequest(int roomId, int lotteryId)
        {
            string csrf_token = null;
            foreach (Cookie cookie in BiliApi.LoginCookies)
            {
                if (cookie.Name == "bili_jct")
                {
                    csrf_token = cookie.Value;
                    break;
                }
            }

            string url = "https://api.live.bilibili.com/xlive/lottery-interface/v3/guard/join";
            string postContent = $"id={lotteryId}&roomid={roomId}&type=guard&csrf_token={csrf_token}&csrf={csrf_token}&visit_id=";
            byte[] postData = Encoding.ASCII.GetBytes(postContent);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(BiliApi.LoginCookies);
            request.Referer = $"https://live.bilibili.com/{roomId}";

            using (Stream stream = request.GetRequestStream())
                stream.Write(postData, 0, postData.Length);

            string result = null;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
                result = reader.ReadToEnd();

            Json.Value join = Json.Parser.Parse(result);
            if(join["code"] == 0)
            {
                AppendLog($"{join["data"]["award_name"]} x{join["data"]["award_num"]} (Guard)");
            }
            else
            {
                AppendLog($"error >> {join["message"]}");
            }
        }

        private void SendGiftLotteryRequest(int roomId, int lotteryId, string giftType)
        {
            string csrf_token = null;
            foreach (Cookie cookie in BiliApi.LoginCookies)
            {
                if (cookie.Name == "bili_jct")
                {
                    csrf_token = cookie.Value;
                    break;
                }
            }

            string url = $"https://api.live.bilibili.com/xlive/lottery-interface/v5/smalltv/join";
            string postContent = $"id={lotteryId}&roomid={roomId}&type={giftType}&csrf_token={csrf_token}&csrf={csrf_token}&visit_id=";
            byte[] postData = Encoding.ASCII.GetBytes(postContent);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = postData.Length;
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(BiliApi.LoginCookies);
            request.Referer = $"https://live.bilibili.com/{roomId}";

            using (Stream stream = request.GetRequestStream())
                stream.Write(postData, 0, postData.Length);

            string result = null;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
                result = reader.ReadToEnd();

            Json.Value join = Json.Parser.Parse(result);
            if (join["code"] == 0)
            {
                AppendLog($"{join["data"]["award_name"]} x{join["data"]["award_num"]} ({giftType} in Room >> {roomId})");
            }
            else
            {
                AppendLog($"error >> {join["message"]}");
            }
        }

        int cooldown = 200;

        HashSet<int> waittingLotteryIds = new HashSet<int>();

        private void CollectLottery()
        {
            List<RoomInfo> rooms = new List<RoomInfo>();
            
            List<HomepageRoomInfo> lotteryRooms = GetHomepageLotteryRooms();
            rooms.AddRange(lotteryRooms);
            Thread.Sleep(cooldown);

            int[] areaIds = GetAreaIds();
            Thread.Sleep(cooldown);

            foreach (int i in areaIds)
            {
                List<TopListRoomInfo> topRooms = GetHourTopRooms(i);
                rooms.AddRange(topRooms);
                Thread.Sleep(cooldown);
            }
            
            foreach (RoomInfo room in rooms)
            {
                AppendLog(room.ToString());
                int roomId = room.RoomId;
                CollectLottery(roomId);
            }
        }

        private void CollectLottery(int roomId)
        {
            
            Json.Value lottery = GetRoomLotteries(roomId);
            Thread.Sleep(cooldown);

            if (lottery["code"] == 0)
            {
                Json.Value data = lottery["data"];

                // pk

                Json.Value pks = data["pk"];
                foreach (Json.Value pk in pks)
                {
                    int lotteryId = pk["pk_id"];
                    if (waittingLotteryIds.Contains(lotteryId))
                        continue;
                    waittingLotteryIds.Add(lotteryId);
                    int waitTime = pk["time_wait"];
                    AppendLog($"Waiting >> Pk (for {waitTime} secs) in Room >> {roomId}");
                    Task.Factory.StartNew(async () =>
                    {
                        await Task.Delay((waitTime + 1) * 1000);
                        SendPkLotteryRequest(roomId, lotteryId);
                        waittingLotteryIds.Remove(lotteryId);
                    });
                    Thread.Sleep(cooldown);
                }

                // gift

                Json.Value gifts = data["gift"];
                foreach (Json.Value gift in gifts)
                {
                    int lotteryId = gift["raffleId"];
                    if (waittingLotteryIds.Contains(lotteryId))
                        continue;
                    waittingLotteryIds.Add(lotteryId);
                    string giftType = gift["type"];
                    int waitTime = gift["time_wait"];
                    AppendLog($"Waiting >> {giftType} (for {waitTime} secs) in Room >> {roomId}");
                    Task.Factory.StartNew(async () =>
                    {
                        await Task.Delay((waitTime + 1) * 1000);
                        SendGiftLotteryRequest(roomId, lotteryId, giftType);
                        waittingLotteryIds.Remove(lotteryId);
                    });

                    Thread.Sleep(cooldown);
                }

                // new guard
                Json.Value guards = data["guard"];
                foreach (Json.Value guard in guards)
                {
                    int lotteryId = guard["id"];
                    SendGuardLotteryRequest(roomId, lotteryId);
                    Thread.Sleep(cooldown);
                }

                
            }
            else
            {
                AppendLog($"error >> {lottery["message"]}");
            }
        }

        private class GiftBag
        {
            public int BagId { get; set; }
            public string CornerMark { get; set; }
            public int GiftId { get; set; }
            public string GiftName { get; set; }
            public int GiftNum { get; set; }

            public GiftBag(Json.Value json)
            {
                BagId = json["bag_id"];
                CornerMark = json["corner_mark"];
                GiftId = json["gift_id"];
                GiftName = json["gift_name"];
                GiftNum = json["gift_num"];
            }

            public override string ToString()
            {
                return $"{GiftName} x{GiftNum}, {CornerMark}";
            }
        }

        private List<GiftBag> GetBagList()
        {
            Json.Value bagList = BiliApi.GetJsonResult("https://api.live.bilibili.com/xlive/web-room/v1/gift/bag_list", null, false);
            if(bagList["code"] == 0)
            {
                List<GiftBag> giftBags = new List<GiftBag>();
                foreach(Json.Value bag in bagList["data"]["list"])
                {
                    giftBags.Add(new GiftBag(bag));
                }
                return giftBags;
            }
            else
            {
                AppendLog($"error >> {bagList["message"]}");
                return null;
            }
        }

        private void ShowBagList()
        {
            List<GiftBag> giftBags = GetBagList();
            foreach (GiftBag giftBag in giftBags)
            {
                AppendLog(giftBag.ToString());
            }
        }

        private Task ShowBagListAsync()
        {
            Task t = Task.Factory.StartNew(() =>
            {
                ShowBagList();
            });
            return t;
        }

        private Task CollectLotteryAsync()
        {
            Task t = Task.Factory.StartNew(() =>
            {
                CollectLottery();
            });
            return t;
        }

        private bool Login()
        {
            MoblieLoginWindow moblieLoginWindow = new MoblieLoginWindow(this);
            moblieLoginWindow.LoggedIn += MoblieLoginWindow_LoggedIn;
            moblieLoginWindow.ShowDialog();
            return BiliApi.IsLoggedIn;
        }

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            Login();
        }

        private void MoblieLoginWindow_LoggedIn(MoblieLoginWindow sender, CookieCollection cookies, uint uid)
        {
            BiliApi.LoginCookies = cookies;
            UserInfo userInfo = UserInfo.GetUserInfo(cookies);
            Dispatcher.Invoke(() =>
            {
                UserInfoBox.Text = userInfo.Uname;
                sender.Close();
            });
            using (FileStream fileStream = new FileStream("login.dat", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                BinaryFormatter binaryFormatter = new BinaryFormatter();
                binaryFormatter.Serialize(fileStream, cookies);
            }
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("Begin...");
            if (!BiliApi.IsLoggedIn)
                if (!Login())
                    return;

            ScanBtn.IsEnabled = false;

            await CollectLotteryAsync();
            AppendLog("Complete !!!");
            AppendLog(string.Empty);
            await Task.Delay(cooldown);

            await ShowBagListAsync();
            AppendLog(string.Empty);
            await Task.Delay(cooldown);

            ScanBtn.IsEnabled = true;
        }

        private void AppendLog(string text)
        {
            Console.WriteLine(text);
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(text + "\n");
                LogBox.ScrollToEnd();
            });
        }

        BiliLiveListener biliLiveListener = null;
        private void ListenBtn_Click(object sender, RoutedEventArgs e)
        {
            ListenBtn.IsEnabled = false;
            if (biliLiveListener == null)
            {
                ListenBtn.Content = "Connecting";
                biliLiveListener = new BiliLiveListener(2746439, BiliLiveListener.Protocols.Tcp);
                biliLiveListener.Connected += BiliLiveListener_Connected;
                biliLiveListener.ConnectionFailed += BiliLiveListener_ConnectionFailed;
                biliLiveListener.JsonsRecieved += BiliLiveListener_JsonsRecieved;
                biliLiveListener.Connect();
            }
            else
            {
                ListenBtn.Content = "Disconnecting";
                biliLiveListener.Disconnected += BiliLiveListener_Disconnected;
                biliLiveListener.Disconnect();
                biliLiveListener = null;
            }
            
        }

        private void BiliLiveListener_Disconnected()
        {
            ListenBtn.IsEnabled = true;
            ListenBtn.Content = "Listen";
            AppendLog("Disconnected");
        }

        private void BiliLiveListener_ConnectionFailed(string message)
        {
            ListenBtn.IsEnabled = true;
            ListenBtn.Content = "Listen";
            AppendLog($"ConnectionFailed {message}");
        }

        private void BiliLiveListener_Connected()
        {
            ListenBtn.IsEnabled = true;
            ListenBtn.Content = "Stop";
            AppendLog("Connected");
        }

        private void BiliLiveListener_JsonsRecieved(Json.Value[] jsons)
        {
            foreach(Json.Value json in jsons)
            {
                Console.WriteLine(json.ToString());

                if(json["cmd"] == "NOTICE_MSG")
                {
                    int roomId = json["roomid"];
                    AppendLog($"RoomId >> {roomId}");
                    CollectLottery(roomId);
                }
            }
        }

        private async void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            await ShowBagListAsync();
            AppendLog(string.Empty);
            await Task.Delay(cooldown);
        }
    }
}

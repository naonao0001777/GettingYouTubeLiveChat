﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Reflection;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.YouTube.v3;
using System.Threading.Tasks;
using System.Configuration;
using System.IO;
using System.Text;
using System.Web.UI;

namespace WebApplication1.Models
{
    public class YoutubeAPI :System.Web.UI.Page
    {
        /// <summary>
        ///  メッセージ
        /// </summary>
        public string ChatMessage { get; set; }

        /// <summary>
        /// 名前
        /// </summary>
        public string ChatName { get; set; }

        /// <summary>
        /// メッセージリスト
        /// </summary>
        public List<string> MessageList { get; set; }

        /// <summary>
        /// 名前リスト
        /// </summary>
        public List<string> NameList { get; set; }

        /// <summary>
        /// ページトークン生成最大件数
        /// </summary>
        public static int maxCountCreatePageToken = 7;

        /// <summary>
        /// チャンネルの存在可否
        /// </summary>
        public static bool isExistChannelId = true;

        /// <summary>
        /// APIキー
        /// </summary>
        private string API_KEY = ConfigurationManager.AppSettings["YouTubeAPIKey"];

        /// <summary>
        /// YouTubeAPI共通部品
        /// </summary>
        /// <param name="param"></param>
        /// <param name="quantity"></param>
        /// <param name="service"></param>
        /// <returns></returns>
        public async Task<LiveChatModelList> IndexYoutube(string param, string quantity, string service)
        {
            LiveChatModelList lcmL = new LiveChatModelList();
            try
            {
                // APIキーからYouTubeのサービスを取得する
                var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    ApiKey = API_KEY
                });

                // YoutubeAPIサービスを処理分け
                if (service == "Live")
                {
                    // YoutubeLiveの特殊IDを取得する
                    string liveChatId = GetLiveChatID(param, youtubeService);

                    // 取得したメッセージを返す
                    lcmL = await GetLiveChatMessage(liveChatId, youtubeService, null, quantity);
                }
                else if (service == "Search")
                {
                    lcmL = await CommentSearch(param, youtubeService, "");
                }
                else
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                string errorMessage = e.Message;
            }

            return lcmL;
        }

        /// <summary>
        /// 動画IDを取得する
        /// </summary>
        /// <param name="videoId"></param>
        /// <param name="youTubeService"></param>
        /// <returns></returns>
        static public string GetLiveChatID(string videoId, YouTubeService youTubeService)
        {
            // ライブの詳細を取得する
            var videosList = youTubeService.Videos.List("LiveStreamingDetails");

            videosList.Id = videoId;

            var videoListResponse = videosList.Execute();

            foreach (var videoID in videoListResponse.Items)
            {
                return videoID.LiveStreamingDetails.ActiveLiveChatId;
            }

            return null;
        }

        /// <summary>
        /// 動画コメントを取得する
        /// </summary>
        /// <param name="liveChatId"></param>
        /// <param name="youtubeService"></param>
        /// <param name="nextPageToken"></param>
        /// <returns></returns>
        static public async Task<LiveChatModelList> GetLiveChatMessage(string liveChatId, YouTubeService youtubeService, string nextPageToken_, string quantity)
        {
            // 結果を入れるリスト
            LiveChatModelList list = new LiveChatModelList();

            // 一時的なリスト
            List<LiveChatModel> instantList = new List<LiveChatModel>();

            // レスポンスリスト
            Google.Apis.YouTube.v3.Data.LiveChatMessageListResponse liveChatResponse;

            // LiveChatMessageトークンを取得する
            var liveChatRequest = youtubeService.LiveChatMessages.List(liveChatId, "snippet,authorDetails");

            // IDのリクエストがとれなかったら処理を終了する
            if (liveChatRequest.LiveChatId == null)
            {
                // チャンネルIDはなし
                isExistChannelId = false;

                return list;
            }
            else
            {
                isExistChannelId = true;
            }

            maxCountCreatePageToken = int.Parse(quantity) / 75;

            // 最終ページトークンまで再帰的な取得をする
            for (int i = 0; i < maxCountCreatePageToken; i++)
            {
                // 次回ページトークン
                liveChatRequest.PageToken = nextPageToken_;

                // リクエストする
                liveChatResponse = liveChatRequest.Execute();

                if (liveChatResponse.Items.Count == 0)
                {
                    break;
                }

                // 再帰的にチャットを取得
                foreach (var liveChat in liveChatResponse.Items)
                {
                    try
                    {
                        LiveChatModel lcInfo = new LiveChatModel();
                        lcInfo.DspMessage = liveChat.Snippet.DisplayMessage;
                        lcInfo.DspName = liveChat.AuthorDetails.DisplayName;
                        lcInfo.ChannelUrl = liveChat.AuthorDetails.ChannelUrl;
                        lcInfo.ChatDateTime = liveChat.Snippet.PublishedAt;
                        lcInfo.ProfileImageUrl = liveChat.AuthorDetails.ProfileImageUrl;

                        instantList.Add(lcInfo);

                    }
                    catch (Exception ext)
                    {
                        string errMessage = ext.Message;
                        Console.Write(errMessage);
                    }
                }

                list.ChatList = instantList;

                // YouTubeのサービスから反応が返ってくるまで待ち
                await Task.Delay((int)liveChatResponse.PollingIntervalMillis);

                // ページトークンを確保する（初回はnull）
                nextPageToken_ = liveChatResponse.NextPageToken;
            }
            return list;

        }

        /// <summary>
        /// Youtubeの投稿動画コメを取得する
        /// </summary>
        /// <returns></returns>
        public async Task<LiveChatModelList> CommentSearch(string param_videoId, YouTubeService youtubeService, string nextPageToken)
        {
            // IDのあるないを初期化
            isExistChannelId = false;

            // コメントを入れるmodelリスト
            LiveChatModelList commentListModel = new LiveChatModelList();

            //// 返信コメントを入れるmodelリスト
            LiveChatModelList replyCommentListModel = new LiveChatModelList();

            //// 返信コメント一時退避リスト
            List<LiveChatModel> instantReply = new List<LiveChatModel>();

            // 一時的なリスト
            List<LiveChatModel> instantList = new List<LiveChatModel>();

            // 動画コメントをリクエストする
            var request = youtubeService.CommentThreads.List("snippet");

            // コメント数をカウントする
            while (true)
            {
                request.VideoId = param_videoId;
                request.TextFormat = CommentThreadsResource.ListRequest.TextFormatEnum.PlainText;
                request.MaxResults = 100;
                request.PageToken = nextPageToken;

                var response = await request.ExecuteAsync();

                // レスポンスがない場合はここからは処理通らない
                isExistChannelId = true;

                foreach (var item in response.Items)
                {
                    LiveChatModel model = new LiveChatModel();

                    model.DspName = item.Snippet.TopLevelComment.Snippet.AuthorDisplayName;
                    model.DspMessage = item.Snippet.TopLevelComment.Snippet.TextDisplay;
                    model.ChannelUrl = item.Snippet.TopLevelComment.Snippet.AuthorChannelUrl;
                    model.ProfileImageUrl = item.Snippet.TopLevelComment.Snippet.AuthorProfileImageUrl;
                    model.LikeCount = item.Snippet.TopLevelComment.Snippet.LikeCount;
                    model.Id = item.Snippet.TopLevelComment.Id;
                    model.IsChild = false;
                    model.CommentDateRaw = DateTime.Parse(item.Snippet.TopLevelComment.Snippet.PublishedAtRaw);
                    model.ChatDateTime = item.Snippet.TopLevelComment.Snippet.PublishedAt;

                    instantList.Add(model);

                    if (item.Snippet.TotalReplyCount > 0)
                    {
                        await ReplyCommentSearch(param_videoId, youtubeService, model.Id, instantList);
                    }
                }
                commentListModel.ChatList = instantList;

                nextPageToken = response.NextPageToken;

                if (nextPageToken == null)
                {
                    break;
                }
            }

            return commentListModel;
        }

        /// <summary>
        /// 返信コメントをとる
        /// </summary>
        /// <param name="param_videoId"></param>
        /// <param name="youtubeService"></param>
        /// <param name="nextPageToken"></param>
        /// <returns></returns>
        public async Task ReplyCommentSearch(string param_videoId, YouTubeService youtubeService, string parentId, List<LiveChatModel> instantList)
        {
            string nextPageToken = "";

            // コメントを入れるmodelリスト
            LiveChatModelList commentListModel = new LiveChatModelList();

            // 一時的なリスト
            List<LiveChatModel> replyInstantModel = new List<LiveChatModel>();

            // 動画コメントをリクエストする
            var request = youtubeService.Comments.List("snippet");

            while (true)
            {
                request.TextFormat = CommentsResource.ListRequest.TextFormatEnum.PlainText;
                request.MaxResults = 100;
                request.ParentId = parentId;
                request.PageToken = nextPageToken;

                var response = await request.ExecuteAsync();

                foreach (var item in response.Items)
                {
                    LiveChatModel model = new LiveChatModel();

                    model.DspName = item.Snippet.AuthorDisplayName;
                    model.DspMessage = item.Snippet.TextDisplay;
                    model.ChannelUrl = item.Snippet.AuthorChannelUrl;
                    model.ProfileImageUrl = item.Snippet.AuthorProfileImageUrl;
                    model.LikeCount = item.Snippet.LikeCount;
                    model.Id = item.Id;
                    model.ParentId = parentId;
                    model.IsChild = true;
                    string a = item.Snippet.ViewerRating;
                    model.CommentDateRaw = DateTime.Parse(item.Snippet.PublishedAtRaw); 
                    model.ChatDateTime = item.Snippet.PublishedAt;

                    instantList.Add(model);
                }

                nextPageToken = response.NextPageToken;

                if (nextPageToken == null)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// CSVダウンロードをする
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="commentList"></param>
        /// <param name="response"></param>
        public void CsvDownloader(string streamId, LiveChatModelList commentList ,HttpResponseBase response)
        {
            try
            {
                string csvDirectoryPath = @"C:\csv";

                if (!Directory.Exists(csvDirectoryPath))
                {
                    Directory.CreateDirectory(csvDirectoryPath);
                }

                string date = "";
                date = DateTime.Now.ToString("yyyymmddHHmmss");
                string fileName = "csvdownload_" + streamId + "_" + date + ".csv";
                response.Clear();
                response.ContentType = "application/octet-stream";
                response.HeaderEncoding = System.Text.Encoding.GetEncoding("SHIFT-JIS");
                response.AddHeader("Content-Disposition", "attachment; filename="+ fileName);
                
                int count = 1;

                response.Write(string.Format("{0},{1},{2},{3},{4}", "#No.", "ユーザ名", "チャンネルID", "いいね数", "コメント"));

                foreach (var comment in commentList.ChatList)
                {
                    string displayComment = comment.DspMessage.Replace("\r", "").Replace("\n", "");

                    if (comment.IsChild)
                    {
                        response.Write(string.Format("{0},{1},{2},{3},{4}", count, "[返信] " + comment.DspName, comment.ChannelUrl, comment.LikeCount, displayComment));
                    }
                    else
                    {
                        response.Write(string.Format("{0},{1},{2},{3},{4}", count, comment.DspName, comment.ChannelUrl, comment.LikeCount, displayComment));
                    }
                    count++;
                }
                response.End();
                //using (StreamWriter sw = new StreamWriter(csvDirectoryPath + "\\" + "csvdownload_" + streamId + "_" + date + ".csv", false, Encoding.UTF8))
                //{
                //    int count = 1;
                    
                //    sw.WriteLine(string.Format("{0},{1},{2},{3},{4}", "#No.", "ユーザ名", "チャンネルID", "いいね数", "コメント"));
                    
                //    foreach (var comment in commentList.ChatList)
                //    {
                //        string displayComment = comment.DspMessage.Replace("\r", "").Replace("\n", "");

                //        if (comment.IsChild)
                //        {
                //            sw.WriteLine(string.Format("{0},{1},{2},{3},{4}", count, "[返信] " + comment.DspName, comment.ChannelUrl, comment.LikeCount, displayComment));
                //        }
                //        else
                //        {
                //            sw.WriteLine(string.Format("{0},{1},{2},{3},{4}", count, comment.DspName, comment.ChannelUrl, comment.LikeCount, displayComment));
                //        }
                //        count++;
                //    }
                //}
            }
            catch (Exception e)
            {
                Console.Write(e.Message);
            }
        }

        /// <summary>
        /// クライアントIPアドレスを取得する
        /// </summary>
        public static StringBuilder GetClientIpAddress()
        {
            StringBuilder sb = new StringBuilder();
            
            string userIp = HttpContext.Current.Request.UserHostAddress;
            string userHostName = HttpContext.Current.Request.UserHostName;
            string userAgent = HttpContext.Current.Request.UserAgent;
            string sessionId = HttpContext.Current.Session.SessionID;
            string browser = HttpContext.Current.Request.Browser.Browser.ToString();

            sb.AppendLine(userIp);
            sb.AppendLine(userHostName);
            sb.AppendLine(userAgent);
            sb.AppendLine(sessionId);
            sb.AppendLine(browser);

            return sb;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ChatCore.Interfaces;
using ChatCore.Services;
using ChatCore.Services.BiliBili;
using ChatCore.Utilities;

namespace ChatCore.Models.BiliBili
{
	public class BiliBiliChatMessage : IChatMessage
	{
		public string Id { get; internal set; } = "";
		public bool IsSystemMessage { get; internal set; }
		public bool IsActionMessage { get; internal set; }
		public bool IsHighlighted { get; internal set; }
		public bool IsPing { get; internal set; }
		public string Message { get; internal set; } = "";
		public string Username { get; internal set; } = "";
		public int Uid { get; internal set; } = -1;
		public string Content { get; internal set; } = "";
		public IChatUser Sender { get; internal set; } = new BiliBiliChatUser();
		public IChatChannel Channel { get; internal set; } = new BiliBiliChatChannel();
		public IChatEmote[] Emotes { get; internal set; } = Array.Empty<IChatEmote>();
		public ReadOnlyDictionary<string, string> Metadata { get; internal set; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
		public string MessageType { get; private set; } = "";
		private static readonly Dictionary<string, Action<BiliBiliChatMessage, JSONNode>> comands = new Dictionary<string, Action<BiliBiliChatMessage, JSONNode>>();

		static BiliBiliChatMessage()
		{
			CreateCommands();
		}
		public BiliBiliChatMessage(string json, int _room_id)
		{
			var obj = JSON.Parse(json);
			if (obj == null)
			{
				return;
			}
			Id = Guid.NewGuid().ToString();
			IsSystemMessage = false;
			IsActionMessage = false;
			IsHighlighted = false;
			IsPing = false;
			obj["room_id"] = new JSONNumber(_room_id);
			
			CreateMessage(JSON.Parse(json));
		}
		public JSONObject ToJson()
		{
			var obj = new JSONObject();
			obj.Add(nameof(Id), new JSONString(Id));
			obj.Add(nameof(IsSystemMessage), new JSONBool(IsSystemMessage));
			obj.Add(nameof(IsActionMessage), new JSONBool(IsActionMessage));
			obj.Add(nameof(IsActionMessage), new JSONBool(IsActionMessage));
			obj.Add(nameof(IsHighlighted), new JSONBool(IsHighlighted));
			obj.Add(nameof(IsPing), new JSONBool(IsPing));
			obj.Add(nameof(Message), new JSONString(Message));
			obj.Add(nameof(Sender), Sender?.ToJson());
			obj.Add(nameof(Channel), Channel?.ToJson());
			var emotes = new JSONArray();
			foreach (var emote in Emotes)
			{
				emotes.Add(emote.ToJson());
			}
			obj.Add(nameof(Emotes), emotes);
			return obj;
		}

		private static void CreateCommands()
		{
			comands.Add("DANMU_MSG", (b, danmuku) => {
				b.MessageType = "danmuku";
				var info = danmuku["info"].AsArray!;
				b.Uid = info[2][0].AsInt;
				b.Username = info[2][1].Value;
				b.Content = info[1].Value.ToString();

				b.Message = info[1].Value;
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
				//b.Channel = new BiliBiliChatChannel(danmuku);
			});
			comands.Add("DANMU_MSG:4:0:2:2:2:0", (b, danmuku) => {
				b.MessageType = "danmuku";
				var info = danmuku["info"].AsArray!;
				b.Uid = info[2][0].AsInt;
				b.Username = info[2][1].Value;
				b.Content = info[1].Value.ToString();

				b.Message = info[1].Value;
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
				//b.Channel = new BiliBiliChatChannel(danmuku);
			});
			comands.Add("SEND_GIFT", (b, danmuku) => {
				b.MessageType = "gift";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsHighlighted = true;
				var info= new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["medal_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();
				info[2][3][0] = new JSONNumber(data["medal_info"]["medal_level"].AsInt);
				info[2][3][1] = new JSONNumber(data["medal_info"]["medal_name"].Value);

				if (string.IsNullOrEmpty(data["combo_num"].Value))
				{
					b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value;
				}
				else
				{
					b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + " x" + data["combo_num"].Value;
				}
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
			});
			comands.Add("COMBO_END", (b, danmuku) => {
				b.MessageType = "combo_end";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsHighlighted = true;
				var info = new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["medal_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();
				info[2][3][0] = new JSONNumber(data["medal_info"]["medal_level"].AsInt);
				info[2][3][1] = new JSONNumber(data["medal_info"]["medal_name"].Value);

				b.Message = data["action"].Value + (data["gift_num"].AsInt == 0 ? 1 : data["gift_num"].AsInt) + "个" + data["gift_name"].Value + " x" + data["combo_num"].Value;
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
			});
			comands.Add("COMBO_SEND", (b, danmuku) => {
				b.MessageType = "combo_send";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsHighlighted = true;
				var info = new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["medal_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();
				info[2][3][0] = new JSONNumber(data["medal_info"]["medal_level"].AsInt);
				info[2][3][1] = new JSONNumber(data["medal_info"]["medal_name"].Value);

				b.Message = data["action"].Value + (data["gift_num"].AsInt == 0 ? 1 : data["gift_num"].AsInt) + "个" + data["gift_name"].Value + " x" + data["combo_num"];
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
			});
			comands.Add("SUPER_CHAT_MESSAGE", (b, danmuku) => {
				b.MessageType = "super_chat";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["user_info"]["uname"].Value;
				b.Content = data["message"].Value;
				b.IsHighlighted = true;

				var info = new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["user_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();

				b.Message = "【SC (￥" + data["price"].AsInt + ")】" + b.Content;
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
			});
			comands.Add("SUPER_CHAT_MESSAGE_JPN", (b, danmuku) => {
				b.MessageType = "super_chat_japanese";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["user_info"]["uname"].Value;
				b.Content = data["message_jpn"].Value;
				b.IsHighlighted = true;

				var info = new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["user_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();

				b.Message = "【SC (JP￥" + data["price"].AsInt + ")】" + b.Content;
				b.Sender = new BiliBiliChatUser(info, danmuku["room_id"]);
			});
			comands.Add("WELCOME", (b, danmuku) => {
				b.MessageType = "welcome";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsSystemMessage = true;

				b.Message = "欢迎老爷 " + b.Username + " 进入直播间";
			});
			comands.Add("INTERACT_WORD", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsSystemMessage = true;

				switch (data["msg_type"].Value.ToString())
				{
					case "1":
						b.MessageType = "welcome";
						b.Message = "欢迎 " + b.Username + " 进入直播间";
						break;
					case "2":
						b.MessageType = "follow";
						b.Message = "感谢 " + b.Username + " 关注直播间";
						break;
					case "3":
						b.MessageType = "share";
						b.Message = "感谢 " + b.Username + " 分享直播间";
						break;
					case "4":
						b.MessageType = "special_follow";
						b.Message = "感谢 " + b.Username + " 特别关注";
						break;
					case "5":
						b.MessageType = "mutual_follow";
						b.Message = "感谢 " + b.Username + " 相互关注";
						break;
					case "6":
						b.MessageType = "unknown";
						b.Message = "【暂不支持该消息】";
						break;
				}
				
			});
			comands.Add("WELCOME_GUARD", (b, danmuku) => {
				b.MessageType = "welcome_guard";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsSystemMessage = true;
				b.IsHighlighted = true;

				b.Message = "欢迎舰长 " + b.Username + " 进入直播间";
			});
			comands.Add("ENTRY_EFFECT", (b, danmuku) => {
				b.MessageType = "effect";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["copy_writing"].Value.Replace("<%", "").Replace("%>", "");
				b.Content = data["copy_writing"].Value.Replace("<%", "").Replace("%>", "");
				b.IsSystemMessage = true;

				b.Message = b.Content;
			});
			comands.Add("ROOM_RANK", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【打榜】" + data["rank_desc"].Value;
			});
			comands.Add("ROOM_BANNER", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【打榜】" + (string.IsNullOrEmpty(data["bls_rank_info"]["rank_info"]["title"].Value)? "小时榜" : data["bls_rank_info"]["rank_info"]["title"].Value + "-" + data["bls_rank_info"]["team_name"].Value) + " 排名: " + data["bls_rank_info"]["rank_info"]["rank_info"]["rank"].Value;
			});
			comands.Add("ACTIVITY_BANNER_UPDATE_V2", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【横幅】当前分区排名" + data["title"].Value;
			});
			comands.Add("ONLINERANK", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				/*var online_rank = data["list"].AsArray;*/
				b.IsSystemMessage = true;

				b.Message = "【在线排名】当前分区排名" + data["title"].Value;
			});
			comands.Add("ROOM_REAL_TIME_MESSAGE_UPDATE", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【关注】粉丝数" + data["fans"].Value.ToString();
			});
			comands.Add("NOTICE_MSG", (b, danmuku) => {
				b.MessageType = "junk";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【喇叭】" + data["msg_common"].Value;
			});
			comands.Add("ANCHOR_LOT_START", (b, danmuku) => {
				b.MessageType = "anchor_lot_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动开始啦!" + data["require_text"].Value + "赢得" + data["award_name"].Value;
			});
			comands.Add("ANCHOR_LOT_CHECKSTATUS", (b, danmuku) => {
				b.MessageType = "anchor_lot_checkstatus";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动开始啦!";
			});
			comands.Add("ANCHOR_LOT_END", (b, danmuku) => {
				b.MessageType = "anchor_lot_end";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动结束啦!";
			});
			comands.Add("ANCHOR_LOT_AWARD", (b, danmuku) => {
				b.MessageType = "anchor_lot";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;
				var list = data["award_users"].AsArray!;
				var usernameList = "";

				foreach (var item in list)
				{
					usernameList += (item.Value)["uname"].Value.ToString();
				}

				b.Message = "【天选】恭喜" + usernameList + "获得" + data["award_name"].Value;
			});
			comands.Add("RAFFLE_START", (b, danmuku) => {
				b.MessageType = "raffle_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【抽奖】" + data["title"].Value + "开始啦!";
			});
			comands.Add("ROOM_BLOCK_MSG", (b, danmuku) => {
				b.MessageType = "blocklist";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【封禁】" + data["uname"].Value + "(UID: " + data["uid"].Value + ")";
			});
			comands.Add("GUARD_BUY", (b, danmuku) => {
				b.MessageType = "new_guard";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsHighlighted = true;

				b.Message = "感谢 " + b.Username + " 成为 " + data["gift_name"].Value + " 加入舰队~";
			});
			comands.Add("USER_TOAST_MSG", (b, danmuku) => {
				b.MessageType = "new_guard_msg";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsSystemMessage = true;
				b.IsHighlighted = true;

				b.Message = b.Username + " 开通了 " + data["num"].Value + "个" + data["unit"] + "的" + data["role_name"] + " 进入舰队啦";
			});
			comands.Add("GUARD_MSG", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				var broadcast_type = danmuku["broadcast_type"].Value;
				if (broadcast_type != "0")
				{
					b.MessageType = "guard_msg";
					b.Message = "【上舰】" + data["msg"].Value.Replace(":?", "");
				}
				else
				{
					b.MessageType = "junk";
					b.Message = "【上舰广播】" + data["msg"].Value.Replace(":?", "");
				}
				b.IsSystemMessage = true;
			});
			comands.Add("GUARD_LOTTERY_START", (b, danmuku) => {
				b.MessageType = "guard_lottery_msg";
				b.IsSystemMessage = true;

				b.Message = "【抽奖】上舰抽奖开始啦";
			});
			comands.Add("ROOM_CHANGE", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.MessageType = "room_change";
				b.IsSystemMessage = true;

				b.Message = "【变更】直播间名称为: " + data["title"].Value;
			});
			comands.Add("PREPARING", (b, danmuku) => {
				b.MessageType = "room_preparing";
				b.IsSystemMessage = true;

				b.Message = "【下播】直播间准备中";
			});
			comands.Add("LIVE", (b, danmuku) => {
				b.MessageType = "room_live";
				b.IsSystemMessage = true;

				b.Message = "【开播】直播间开播啦";
			});
			comands.Add("WARNING", (b, danmuku) => {
				b.MessageType = "warning";
				b.IsHighlighted = true;

				b.Message = "【超管】" + danmuku["msg"]?.Value;
			});
			comands.Add("CUT_OFF", (b, danmuku) => {
				b.MessageType = "cut_off";
				b.IsHighlighted = true;

				b.Message = "【切断】" + danmuku["msg"]?.Value;
			});
			comands.Add("STOP_LIVE_ROOM_LIST", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.MessageType = "junk";
				b.IsSystemMessage = true;

				b.Message = "以下房间停止直播：" + data["room_id_list"].AsArray!.ToString();
			});

			/*comands.Add("GIFT_TOP", (b, danmuku) => {
				// 高能榜
			});*/
		}

		private void CreateMessage(JSONNode danmuku)
		{
			if (comands.TryGetValue(danmuku["cmd"].Value, out var commandAction))
			{
				commandAction?.Invoke(this, danmuku);
			}
			else
			{
				MessageType = "unkown";
				Message = "【暂不支持该消息】";
				IsSystemMessage = true;
			}
		}

		public void BanMessage() {
			MessageType = "banned";
		}
	}
}

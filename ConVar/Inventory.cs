using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;

namespace ConVar;

[Factory("inventory")]
public class Inventory : ConsoleSystem
{
	public class SavedLoadout
	{
		public struct SavedItem
		{
			public int id;

			public int amount;

			public ulong skin;

			public int[] containedItems;
		}

		public SavedItem[] belt;

		public SavedItem[] wear;

		public int heldItemIndex;

		public SavedLoadout()
		{
		}

		public SavedLoadout(BasePlayer player)
		{
			belt = SaveItems(player.inventory.containerBelt);
			wear = SaveItems(player.inventory.containerWear);
			heldItemIndex = GetSlotIndex(player);
		}

		private static SavedItem[] SaveItems(ItemContainer itemContainer)
		{
			List<SavedItem> list = new List<SavedItem>();
			for (int i = 0; i < itemContainer.capacity; i++)
			{
				Item slot = itemContainer.GetSlot(i);
				if (slot == null)
				{
					continue;
				}
				SavedItem savedItem = default(SavedItem);
				savedItem.id = slot.info.itemid;
				savedItem.amount = slot.amount;
				savedItem.skin = slot.skin;
				SavedItem item = savedItem;
				if (slot.contents != null && slot.contents.itemList != null)
				{
					List<int> list2 = new List<int>();
					foreach (Item item2 in slot.contents.itemList)
					{
						list2.Add(item2.info.itemid);
					}
					item.containedItems = list2.ToArray();
				}
				list.Add(item);
			}
			return list.ToArray();
		}

		public void LoadItemsOnTo(BasePlayer player)
		{
			player.inventory.containerBelt.Clear();
			player.inventory.containerWear.Clear();
			SavedItem[] array = belt;
			foreach (SavedItem item in array)
			{
				player.inventory.GiveItem(LoadItem(item), player.inventory.containerBelt);
			}
			array = wear;
			foreach (SavedItem item2 in array)
			{
				player.inventory.GiveItem(LoadItem(item2), player.inventory.containerWear);
			}
			EquipItemInSlot(player, heldItemIndex);
		}

		private Item LoadItem(SavedItem item)
		{
			Item item2 = ItemManager.CreateByItemID(item.id, item.amount, item.skin);
			if (item.containedItems != null && item.containedItems.Length != 0)
			{
				int[] containedItems = item.containedItems;
				foreach (int itemID in containedItems)
				{
					item2.contents.AddItem(ItemManager.FindItemDefinition(itemID), 1, 0uL);
				}
			}
			return item2;
		}
	}

	private const string LoadoutDirectory = "loadouts";

	[ServerVar(Help = "Disables all attire limitations, so NPC clothing and invalid overlaps can be equipped")]
	public static bool disableAttireLimitations;

	[ServerUserVar]
	public static void lighttoggle(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((bool)basePlayer && !basePlayer.IsDead() && !basePlayer.IsSleeping() && !basePlayer.InGesture)
		{
			basePlayer.LightToggle();
		}
	}

	[ServerUserVar]
	public static void endloot(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((bool)basePlayer && !basePlayer.IsDead() && !basePlayer.IsSleeping())
		{
			basePlayer.inventory.loot.Clear();
		}
	}

	[ServerVar]
	public static void give(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!basePlayer)
		{
			return;
		}
		Item item = ItemManager.CreateByPartialName(arg.GetString(0), 1, arg.GetULong(3, 0uL));
		if (item == null)
		{
			arg.ReplyWith("Invalid Item!");
			return;
		}
		int num = (item.amount = arg.GetInt(1, 1));
		float num2 = (item.conditionNormalized = arg.GetFloat(2, 1f));
		item.OnVirginSpawn();
		if (!basePlayer.inventory.GiveItem(item))
		{
			item.Remove();
			arg.ReplyWith("Couldn't give item (inventory full?)");
			return;
		}
		basePlayer.Command("note.inv", item.info.itemid, num);
		Debug.Log("giving " + basePlayer.displayName + " " + num + " x " + item.info.displayName.english);
		if (basePlayer.IsDeveloper)
		{
			basePlayer.ChatMessage("you silently gave yourself " + num + " x " + item.info.displayName.english);
		}
		else
		{
			Chat.Broadcast(basePlayer.displayName + " gave themselves " + num + " x " + item.info.displayName.english, "SERVER", "#eee", 0uL);
		}
	}

	[ServerVar]
	public static void resetbp(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((bool)basePlayer)
		{
			basePlayer.blueprints.Reset();
		}
	}

	[ServerVar]
	public static void unlockall(Arg arg)
	{
		BasePlayer basePlayer = arg.GetPlayer(0) ?? arg.Player();
		if (!basePlayer)
		{
			arg.ReplyWith("Can't find player");
		}
		else
		{
			basePlayer.blueprints.UnlockAll();
		}
	}

	[ServerVar]
	public static void giveall(Arg arg)
	{
		Item item = null;
		string text = "SERVER";
		if (arg.Player() != null)
		{
			text = arg.Player().displayName;
		}
		foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
		{
			item = ItemManager.CreateByPartialName(arg.GetString(0), 1, 0uL);
			if (item == null)
			{
				arg.ReplyWith("Invalid Item!");
				return;
			}
			int num = (item.amount = arg.GetInt(1, 1));
			item.OnVirginSpawn();
			if (!activePlayer.inventory.GiveItem(item))
			{
				item.Remove();
				arg.ReplyWith("Couldn't give item (inventory full?)");
				continue;
			}
			activePlayer.Command("note.inv", item.info.itemid, num);
			Debug.Log(" [ServerVar] giving " + activePlayer.displayName + " " + item.amount + " x " + item.info.displayName.english);
		}
		if (item != null)
		{
			Chat.Broadcast(text + " gave everyone " + item.amount + " x " + item.info.displayName.english, "SERVER", "#eee", 0uL);
		}
	}

	[ServerVar]
	public static void giveto(Arg arg)
	{
		string text = "SERVER";
		if (arg.Player() != null)
		{
			text = arg.Player().displayName;
		}
		BasePlayer basePlayer = BasePlayer.Find(arg.GetString(0));
		if (basePlayer == null)
		{
			arg.ReplyWith("Couldn't find player!");
			return;
		}
		Item item = ItemManager.CreateByPartialName(arg.GetString(1), 1, arg.GetULong(3, 0uL));
		if (item == null)
		{
			arg.ReplyWith("Invalid Item!");
			return;
		}
		int num = (item.amount = arg.GetInt(2, 1));
		item.OnVirginSpawn();
		if (!basePlayer.inventory.GiveItem(item))
		{
			item.Remove();
			arg.ReplyWith("Couldn't give item (inventory full?)");
			return;
		}
		basePlayer.Command("note.inv", item.info.itemid, num);
		Debug.Log(" [ServerVar] giving " + basePlayer.displayName + " " + num + " x " + item.info.displayName.english);
		Chat.Broadcast(text + " gave " + basePlayer.displayName + " " + num + " x " + item.info.displayName.english, "SERVER", "#eee", 0uL);
	}

	[ServerVar]
	public static void giveid(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!basePlayer)
		{
			return;
		}
		Item item = ItemManager.CreateByItemID(arg.GetInt(0), 1, 0uL);
		if (item == null)
		{
			arg.ReplyWith("Invalid Item!");
			return;
		}
		int num = (item.amount = arg.GetInt(1, 1));
		item.OnVirginSpawn();
		if (!basePlayer.inventory.GiveItem(item))
		{
			item.Remove();
			arg.ReplyWith("Couldn't give item (inventory full?)");
			return;
		}
		basePlayer.Command("note.inv", item.info.itemid, num);
		Debug.Log(" [ServerVar] giving " + basePlayer.displayName + " " + num + " x " + item.info.displayName.english);
		if (basePlayer.IsDeveloper)
		{
			basePlayer.ChatMessage("you silently gave yourself " + num + " x " + item.info.displayName.english);
		}
		else
		{
			Chat.Broadcast(basePlayer.displayName + " gave themselves " + num + " x " + item.info.displayName.english, "SERVER", "#eee", 0uL);
		}
	}

	[ServerVar]
	public static void givearm(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!basePlayer)
		{
			return;
		}
		Item item = ItemManager.CreateByItemID(arg.GetInt(0), 1, 0uL);
		if (item == null)
		{
			arg.ReplyWith("Invalid Item!");
			return;
		}
		int num = (item.amount = arg.GetInt(1, 1));
		item.OnVirginSpawn();
		if (!basePlayer.inventory.GiveItem(item, basePlayer.inventory.containerBelt))
		{
			item.Remove();
			arg.ReplyWith("Couldn't give item (inventory full?)");
			return;
		}
		basePlayer.Command("note.inv", item.info.itemid, num);
		Debug.Log(" [ServerVar] giving " + basePlayer.displayName + " " + item.amount + " x " + item.info.displayName.english);
		if (basePlayer.IsDeveloper)
		{
			basePlayer.ChatMessage("you silently gave yourself " + item.amount + " x " + item.info.displayName.english);
		}
		else
		{
			Chat.Broadcast(basePlayer.displayName + " gave themselves " + item.amount + " x " + item.info.displayName.english, "SERVER", "#eee", 0uL);
		}
	}

	[ServerVar(Help = "Copies the players inventory to the player in front of them")]
	public static void copyTo(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((!basePlayer.IsAdmin && !basePlayer.IsDeveloper && !Server.cinematic) || basePlayer == null)
		{
			return;
		}
		BasePlayer basePlayer2 = null;
		if (arg.HasArgs() && arg.GetString(0).ToLower() != "true")
		{
			basePlayer2 = arg.GetPlayer(0);
			if (basePlayer2 == null)
			{
				uint uInt = arg.GetUInt(0);
				basePlayer2 = BasePlayer.FindByID(uInt);
				if (basePlayer2 == null)
				{
					basePlayer2 = BasePlayer.FindBot(uInt);
				}
			}
		}
		else
		{
			basePlayer2 = RelationshipManager.GetLookingAtPlayer(basePlayer);
		}
		if (basePlayer2 == null)
		{
			return;
		}
		basePlayer2.inventory.containerBelt.Clear();
		basePlayer2.inventory.containerWear.Clear();
		int num = 0;
		foreach (Item item2 in basePlayer.inventory.containerBelt.itemList)
		{
			basePlayer2.inventory.containerBelt.AddItem(item2.info, item2.amount, item2.skin);
			if (item2.contents != null)
			{
				Item item = basePlayer2.inventory.containerBelt.itemList[num];
				foreach (Item item3 in item2.contents.itemList)
				{
					item.contents.AddItem(item3.info, item3.amount, item3.skin);
				}
			}
			num++;
		}
		foreach (Item item4 in basePlayer.inventory.containerWear.itemList)
		{
			basePlayer2.inventory.containerWear.AddItem(item4.info, item4.amount, item4.skin);
		}
		if (basePlayer.IsDeveloper)
		{
			basePlayer.ChatMessage("you silently copied items to " + basePlayer2.displayName);
		}
		else
		{
			Chat.Broadcast(basePlayer.displayName + " copied their inventory to " + basePlayer2.displayName, "SERVER", "#eee", 0uL);
		}
	}

	[ServerVar(Help = "Deploys a loadout to players in a radius eg. inventory.deployLoadoutInRange testloadout 30")]
	public static void deployLoadoutInRange(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((!basePlayer.IsAdmin && !basePlayer.IsDeveloper && !Server.cinematic) || basePlayer == null)
		{
			return;
		}
		string @string = arg.GetString(0);
		if (!LoadLoadout(@string, out var so))
		{
			arg.ReplyWith("Can't find loadout: " + @string);
			return;
		}
		float @float = arg.GetFloat(1);
		List<BasePlayer> obj = Facepunch.Pool.GetList<BasePlayer>();
		global::Vis.Entities(basePlayer.transform.position, @float, obj, 131072);
		int num = 0;
		foreach (BasePlayer item in obj)
		{
			if (!(item == basePlayer) && !item.isClient)
			{
				so.LoadItemsOnTo(item);
				num++;
			}
		}
		arg.ReplyWith($"Applied loadout {@string} to {num} players");
		Facepunch.Pool.FreeList(ref obj);
	}

	[ServerVar(Help = "Deploys the given loadout to a target player. eg. inventory.deployLoadout testloadout jim")]
	public static void deployLoadout(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!(basePlayer == null) && (basePlayer.IsAdmin || basePlayer.IsDeveloper || Server.cinematic))
		{
			string @string = arg.GetString(0);
			BasePlayer basePlayer2 = arg.GetPlayerOrSleeperOrBot(1);
			if (basePlayer2 == null)
			{
				basePlayer2 = basePlayer;
			}
			SavedLoadout so;
			if (basePlayer2 == null)
			{
				arg.ReplyWith("Could not find player " + arg.GetString(1) + " and no local player available");
			}
			else if (LoadLoadout(@string, out so))
			{
				so.LoadItemsOnTo(basePlayer2);
				arg.ReplyWith("Deployed loadout " + @string + " to " + basePlayer2.displayName);
			}
			else
			{
				arg.ReplyWith("Could not find loadout " + @string);
			}
		}
	}

	[ServerVar(Help = "Clears the inventory of a target player. eg. inventory.clearInventory jim")]
	public static void clearInventory(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!(basePlayer == null) && (basePlayer.IsAdmin || basePlayer.IsDeveloper || Server.cinematic))
		{
			BasePlayer basePlayer2 = arg.GetPlayerOrSleeperOrBot(0);
			if (basePlayer2 == null)
			{
				basePlayer2 = basePlayer;
			}
			if (basePlayer2 == null)
			{
				arg.ReplyWith("Could not find player " + arg.GetString(1) + " and no local player available");
				return;
			}
			basePlayer2.inventory.containerBelt.Clear();
			basePlayer2.inventory.containerWear.Clear();
			basePlayer2.inventory.containerMain.Clear();
		}
	}

	private static string GetLoadoutPath(string loadoutName)
	{
		return Server.GetServerFolder("loadouts") + "/" + loadoutName + ".ldt";
	}

	[ServerVar(Help = "Saves the current equipped loadout of the calling player. eg. inventory.saveLoadout loaduoutname")]
	public static void saveloadout(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (!(basePlayer == null) && (basePlayer.IsAdmin || basePlayer.IsDeveloper || Server.cinematic))
		{
			string @string = arg.GetString(0);
			string contents = JsonConvert.SerializeObject(new SavedLoadout(basePlayer), Formatting.Indented);
			string loadoutPath = GetLoadoutPath(@string);
			File.WriteAllText(loadoutPath, contents);
			arg.ReplyWith("Saved loadout to " + loadoutPath);
		}
	}

	public static bool LoadLoadout(string name, out SavedLoadout so)
	{
		so = new SavedLoadout();
		string loadoutPath = GetLoadoutPath(name);
		if (!File.Exists(loadoutPath))
		{
			return false;
		}
		so = JsonConvert.DeserializeObject<SavedLoadout>(File.ReadAllText(loadoutPath));
		if (so == null)
		{
			return false;
		}
		return true;
	}

	[ServerVar(Help = "Prints all saved inventory loadouts")]
	public static void listloadouts(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if (basePlayer == null || (!basePlayer.IsAdmin && !basePlayer.IsDeveloper && !Server.cinematic))
		{
			return;
		}
		string serverFolder = Server.GetServerFolder("loadouts");
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in Directory.EnumerateFiles(serverFolder))
		{
			stringBuilder.AppendLine(item);
		}
		arg.ReplyWith(stringBuilder.ToString());
	}

	[ClientVar]
	[ServerVar]
	public static void defs(Arg arg)
	{
		if (Steamworks.SteamInventory.Definitions == null)
		{
			arg.ReplyWith("no definitions");
			return;
		}
		if (Steamworks.SteamInventory.Definitions.Length == 0)
		{
			arg.ReplyWith("0 definitions");
			return;
		}
		string[] obj = Steamworks.SteamInventory.Definitions.Select((InventoryDef x) => x.Name).ToArray();
		arg.ReplyWith(obj);
	}

	[ClientVar]
	[ServerVar]
	public static void reloaddefs(Arg arg)
	{
		Steamworks.SteamInventory.LoadItemDefinitions();
	}

	[ServerVar]
	public static void equipslottarget(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((basePlayer.IsAdmin || basePlayer.IsDeveloper || Server.cinematic) && !(basePlayer == null))
		{
			BasePlayer lookingAtPlayer = RelationshipManager.GetLookingAtPlayer(basePlayer);
			if (!(lookingAtPlayer == null))
			{
				int @int = arg.GetInt(0);
				EquipItemInSlot(lookingAtPlayer, @int);
				arg.ReplyWith($"Equipped slot {@int} on player {lookingAtPlayer.displayName}");
			}
		}
	}

	[ServerVar]
	public static void equipslot(Arg arg)
	{
		BasePlayer basePlayer = arg.Player();
		if ((!basePlayer.IsAdmin && !basePlayer.IsDeveloper && !Server.cinematic) || basePlayer == null)
		{
			return;
		}
		BasePlayer basePlayer2 = null;
		if (arg.HasArgs(2))
		{
			basePlayer2 = arg.GetPlayer(1);
			if (basePlayer2 == null)
			{
				uint uInt = arg.GetUInt(1);
				basePlayer2 = BasePlayer.FindByID(uInt);
				if (basePlayer2 == null)
				{
					basePlayer2 = BasePlayer.FindBot(uInt);
				}
			}
		}
		if (!(basePlayer2 == null))
		{
			int @int = arg.GetInt(0);
			EquipItemInSlot(basePlayer2, @int);
			Debug.Log($"Equipped slot {@int} on player {basePlayer2.displayName}");
		}
	}

	private static void EquipItemInSlot(BasePlayer player, int slot)
	{
		uint itemID = 0u;
		for (int i = 0; i < player.inventory.containerBelt.itemList.Count; i++)
		{
			if (player.inventory.containerBelt.itemList[i] != null && i == slot)
			{
				itemID = player.inventory.containerBelt.itemList[i].uid;
				break;
			}
		}
		player.UpdateActiveItem(itemID);
	}

	private static int GetSlotIndex(BasePlayer player)
	{
		if (player.GetActiveItem() == null)
		{
			return -1;
		}
		uint uid = player.GetActiveItem().uid;
		for (int i = 0; i < player.inventory.containerBelt.itemList.Count; i++)
		{
			if (player.inventory.containerBelt.itemList[i] != null && player.inventory.containerBelt.itemList[i].uid == uid)
			{
				return i;
			}
		}
		return -1;
	}
}

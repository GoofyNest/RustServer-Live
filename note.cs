using Facepunch.Extend;
using UnityEngine;

[Factory("note")]
public class note : ConsoleSystem
{
	[ServerUserVar]
	public static void update(Arg arg)
	{
		uint uInt = arg.GetUInt(0);
		string @string = arg.GetString(1);
		Item item = arg.Player().inventory.FindItemUID(uInt);
		if (item != null)
		{
			item.text = @string.Truncate(1024);
			item.MarkDirty();
		}
	}
}

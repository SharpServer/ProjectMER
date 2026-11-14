namespace ProjectMER.Features.Serializable.Lockers;

public class SerializableLockerLoot
{
	public SerializableLockerLoot() { }

	public SerializableLockerLoot(ItemType targetItem, int remainingUses, int maxPerChamber, int probabilityPoints, int minPerChamber)
	{
		TargetItem = targetItem;
		RemainingUses = remainingUses;
		MaxPerChamber = maxPerChamber;
		ProbabilityPoints = probabilityPoints;
		MinPerChamber = minPerChamber;
	}

	public ItemType TargetItem { get; set; }

	public string CustomItem { get; set; } = string.Empty;

	public int RemainingUses { get; set; }

	public int MaxPerChamber { get; set; }

	public int ProbabilityPoints { get; set; }

	public int MinPerChamber { get; set; } = 1;

	/// <summary>
	/// 弾薬（Ammo*）の1ピックアップあたりの弾数。0 以下ならゲーム既定のまま。
	/// Unity 側では値があるときだけ書き出す任意フィールド。
	/// </summary>
	public int AmmoPerPickup { get; set; }
}

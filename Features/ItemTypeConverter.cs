using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace ProjectMER.Features;

/// <summary>
/// マップ YAML の <see cref="ItemType"/> を表記ゆれを許容して読み込む。
/// <list type="bullet">
/// <item>大文字小文字・空白・"_"・"-"・"."・先頭の "ItemType" を無視する（"gun_com15" など）</item>
/// <item>数値 ID も受け付ける</item>
/// <item>解決できない値はマップ全体の読み込みを失敗させず、警告 + None にする</item>
/// </list>
/// 標準の enum デシリアライザは "gun_com15" のような表記を拒否し、しかも
/// どの値が悪いのか分からないままマップ全体の読み込みを失敗させてしまう。
/// </summary>
public sealed class ItemTypeConverter : IYamlTypeConverter
{
	/// <inheritdoc cref="IYamlTypeConverter" />
	public bool Accepts(Type type) => type == typeof(ItemType);

	/// <inheritdoc cref="IYamlTypeConverter" />
	public object ReadYaml(IParser parser, Type type)
	{
		string value = parser.Consume<Scalar>().Value;

		if (ItemSpawnSpec.TryResolveItemType(value, out ItemType itemType))
			return itemType;

		Logger.Warn($"Map YAML: {ItemSpawnSpec.DescribeUnknownItem(value)} Falling back to ItemType.None.");
		return ItemType.None;
	}

	/// <inheritdoc cref="IYamlTypeConverter" />
	public void WriteYaml(IEmitter emitter, object? value, Type type)
		=> emitter.Emit(new Scalar(((ItemType)value!).ToString()));
}

namespace ERP.MES.Desktop.ViewModels;

/// <summary>A grid row that stands for one item. The drawing button in every item list reads the item from the row through this (Decision Log #125 Q14).</summary>
public interface IHasItemId
{
    long ItemId { get; }
}

namespace POS.Domain.Enums;

/// <summary>
/// What a <see cref="Models.Catalogs.PlanAddOn"/> unlocks, which gives meaning to
/// <c>LinkedEntityId</c>. For <see cref="DeviceLicense"/>, <c>LinkedEntityId</c> is the
/// integer value of a <see cref="FeatureKey"/> (e.g. MaxKdsScreens=14). Persisted as a
/// string. v2 runtime only consumes <see cref="DeviceLicense"/>; the rest are declared
/// for the catalog but not yet wired to enforcement.
/// </summary>
public enum PlanAddOnLinkType
{
    PaymentMethod,
    Feature,
    BranchSlot,
    DeviceLicense,
    Custom
}

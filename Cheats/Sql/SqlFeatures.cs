namespace FH6Mod.Cheats.Sql;

using System.Linq;

public enum SqlFeature
{
    ClearNewTag,
    FreeCarPrices,
    InstallFlags,
    AddAllCars,
    AutoshowUnlock,
    FreeUpgrades,
    FreeWheels,
    UnlockUpgradePresets,
    FullAutoshow,
    DriftScoreScalar,
    MaxTraction,
    TorqueScale,
    DragScale,
}

/// <summary>
/// Pre-baked SQL bundles. Each maps a high-level feature to one or more
/// queries. Order matters — backup table first, then mutation.
/// </summary>
internal static class SqlFeatureCatalog
{
    public sealed record Feature(string Name, string Description, string[] Queries);

    public static Feature Get(SqlFeature f) => f switch
    {
        SqlFeature.ClearNewTag => new(
            "Clear \"New\" Tag on Cars",
            "Marks every garage car as 'already viewed' so the persistent NEW! badge disappears.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_NewTags AS SELECT CarId, HasCurrentOwnerViewedCar FROM Profile0_Career_Garage;",
                "UPDATE Profile0_Career_Garage SET HasCurrentOwnerViewedCar = 1 WHERE HasCurrentOwnerViewedCar IS NULL OR HasCurrentOwnerViewedCar <> 1;",
            ]),

        SqlFeature.FreeCarPrices => new(
            "Free Car Prices",
            "Sets BaseCost = 0 on every car in Data_Car. New autoshow purchases are free.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_Database_FreeCarPrices AS SELECT Id, BaseCost FROM Data_Car;",
                "UPDATE Data_Car SET BaseCost = 0;",
            ]),

        SqlFeature.InstallFlags => new(
            "Install Flags (all cars installed/purchased/drivable)",
            "Flips IsInstalled/IsPurchased/IsDrivable to 1 for every car in Data_Car. Removes 'install required' gates.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_DataCarIsInstalled AS SELECT Id, IsInstalled FROM Data_Car;",
                "UPDATE Data_Car SET IsInstalled = 1 WHERE IsInstalled IS NULL OR IsInstalled <> 1;",
                "CREATE TABLE IF NOT EXISTS _backup_DataCarIsPurchased AS SELECT Id, IsPurchased FROM Data_Car;",
                "UPDATE Data_Car SET IsPurchased = 1 WHERE IsPurchased IS NULL OR IsPurchased <> 1;",
                "CREATE TABLE IF NOT EXISTS _backup_DataCarIsDrivable AS SELECT Id, IsDrivable FROM Data_Car;",
                "UPDATE Data_Car SET IsDrivable = 1 WHERE IsDrivable IS NULL OR IsDrivable <> 1;",
            ]),

        SqlFeature.AddAllCars => new(
            "Full Autoshow (all cars visible + buckets)",
            "Makes every car appear in the autoshow by filling CarBuckets and recreating the Drivable_Data_Car view.",
            [
                "DROP VIEW IF EXISTS Drivable_Data_Car;",
                "CREATE VIEW Drivable_Data_Car AS SELECT * FROM Data_Car;",
                "INSERT OR IGNORE INTO CarBuckets(CarId) SELECT Id FROM Data_Car WHERE Id NOT IN (SELECT CarId FROM CarBuckets);",
                "UPDATE CarBuckets SET CarBucket=0, BucketHero=0 WHERE CarBucket IS NULL;",
            ]),

        SqlFeature.AutoshowUnlock => new(
            "Autoshow — All Cars Visible",
            "Removes the 'NotAvailableInAutoshow' and 'VisibleOnlyIfOwned' filters; every car shows up in showroom listings.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_AutoshowState AS SELECT Id, NotAvailableInAutoshow, BaseCost FROM Data_Car;",
                "UPDATE Data_Car SET NotAvailableInAutoshow = 0;",
                "CREATE TABLE IF NOT EXISTS _backup_DataCarVisibleOnlyIfOwned AS SELECT Id, VisibleOnlyIfOwned FROM Data_Car;",
                "UPDATE Data_Car SET VisibleOnlyIfOwned = 0;",
            ]),

        SqlFeature.FreeUpgrades => new(
            "Free Upgrades (performance + visual)",
            "Sets price=0 on all 47 upgrade tables — engine, turbo, brakes, body kits, rims, etc.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_FreeUpgrades AS SELECT 1;",
                ..FreeUpgradeTables.Select(t => $"UPDATE [{t}] SET price=0;"),
            ]),

        SqlFeature.FreeWheels => new(
            "Free Wheels",
            "Sets price=1 on all wheels in List_Wheels (1 = free, matching FH5/FH6 convention).",
            [
                "CREATE TABLE IF NOT EXISTS _backup_FreeWheels AS SELECT Id, price FROM List_Wheels;",
                "UPDATE List_Wheels SET price=1;",
            ]),

        SqlFeature.UnlockUpgradePresets => new(
            "Unlock Upgrade Presets",
            "Sets Purchasable=1 on all upgrade preset packages, revealing hidden preset tunes.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_UpgradePresets AS SELECT Id, Purchasable FROM UpgradePresetPackages;",
                "UPDATE UpgradePresetPackages SET Purchasable=1 WHERE Purchasable=0;",
            ]),

        SqlFeature.FullAutoshow => new(
            "Full Autoshow (CarBuckets)",
            "Fills CarBuckets so every car appears in autoshow listings. Use Autoshow Unlock first to make them all visible.",
            [
                "INSERT OR IGNORE INTO CarBuckets(CarId) SELECT Id FROM Data_Car WHERE Id NOT IN (SELECT CarId FROM CarBuckets);",
                "UPDATE CarBuckets SET CarBucket=0, BucketHero=0 WHERE CarBucket IS NULL;",
            ]),

        SqlFeature.DriftScoreScalar => new(
            "Drift Score Scalar (10x)",
            "Sets DriftScoreScalar = 10.0 on all tracks — drift scores are multiplied by 10x.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_DriftScoreScalar AS SELECT rowid, DriftScoreScalar FROM Tracks;",
                "UPDATE Tracks SET DriftScoreScalar = 10.0;",
            ]),

        SqlFeature.MaxTraction => new(
            "Max Traction (Grip Hack)",
            "Sets Traction_Road = 10.0 and WetFriction scales to 5.0 on all cars and tire compounds — massive grip increase.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_MaxTraction_Car AS SELECT Id, Traction_Road FROM Data_Car;",
                "UPDATE Data_Car SET Traction_Road = 10.0;",
                "CREATE TABLE IF NOT EXISTS _backup_MaxTraction_Tire AS SELECT rowid, WetFrictionModFrictionScale, WetOffroadFrictionScale FROM List_TireCompound;",
                "UPDATE List_TireCompound SET WetFrictionModFrictionScale = 5.0, WetOffroadFrictionScale = 5.0;",
            ]),

        SqlFeature.TorqueScale => new(
            "Torque Scale (2x)",
            "Sets GameTorqueScale = 2.0 on all cars — doubles engine torque output.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_TorqueScale AS SELECT Id, GameTorqueScale FROM Data_Car;",
                "UPDATE Data_Car SET GameTorqueScale = 2.0;",
            ]),

        SqlFeature.DragScale => new(
            "Reduce Drag (0.5x)",
            "Sets GameDragScale = 0.5 on all cars — halves aerodynamic drag for higher top speed.",
            [
                "CREATE TABLE IF NOT EXISTS _backup_DragScale AS SELECT Id, GameDragScale FROM Data_Car;",
                "UPDATE Data_Car SET GameDragScale = 0.5;",
            ]),

        _ => throw new System.InvalidOperationException("Unknown SQL feature."),
    };

    /// <summary>
    /// Revert queries — restore from _backup_* tables that <see cref="Get"/> creates.
    /// Used when a toggle-mode lock is turned OFF so the game returns to pre-cheat state.
    /// Returns empty array for features that don't need revert (one-shots).
    /// </summary>
    public static string[] GetRevert(SqlFeature f) => f switch
    {
        SqlFeature.FreeCarPrices =>
        [
            "UPDATE Data_Car SET BaseCost = (SELECT BaseCost FROM _backup_Database_FreeCarPrices WHERE _backup_Database_FreeCarPrices.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_Database_FreeCarPrices WHERE _backup_Database_FreeCarPrices.Id = Data_Car.Id);",
        ],
        SqlFeature.AutoshowUnlock =>
        [
            "UPDATE Data_Car SET NotAvailableInAutoshow = (SELECT NotAvailableInAutoshow FROM _backup_AutoshowState WHERE _backup_AutoshowState.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_AutoshowState WHERE _backup_AutoshowState.Id = Data_Car.Id);",
            "UPDATE Data_Car SET VisibleOnlyIfOwned = (SELECT VisibleOnlyIfOwned FROM _backup_DataCarVisibleOnlyIfOwned WHERE _backup_DataCarVisibleOnlyIfOwned.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_DataCarVisibleOnlyIfOwned WHERE _backup_DataCarVisibleOnlyIfOwned.Id = Data_Car.Id);",
        ],
        SqlFeature.ClearNewTag =>
        [
            "UPDATE Profile0_Career_Garage SET HasCurrentOwnerViewedCar = (SELECT HasCurrentOwnerViewedCar FROM _backup_NewTags WHERE _backup_NewTags.CarId = Profile0_Career_Garage.CarId) WHERE EXISTS (SELECT 1 FROM _backup_NewTags WHERE _backup_NewTags.CarId = Profile0_Career_Garage.CarId);",
        ],
        SqlFeature.InstallFlags =>
        [
            "UPDATE Data_Car SET IsInstalled = (SELECT IsInstalled FROM _backup_DataCarIsInstalled WHERE _backup_DataCarIsInstalled.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_DataCarIsInstalled WHERE _backup_DataCarIsInstalled.Id = Data_Car.Id);",
            "UPDATE Data_Car SET IsPurchased = (SELECT IsPurchased FROM _backup_DataCarIsPurchased WHERE _backup_DataCarIsPurchased.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_DataCarIsPurchased WHERE _backup_DataCarIsPurchased.Id = Data_Car.Id);",
            "UPDATE Data_Car SET IsDrivable = (SELECT IsDrivable FROM _backup_DataCarIsDrivable WHERE _backup_DataCarIsDrivable.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_DataCarIsDrivable WHERE _backup_DataCarIsDrivable.Id = Data_Car.Id);",
        ],
        SqlFeature.FreeUpgrades => [],   // too many tables to revert individually; one-shot is fine
        SqlFeature.AddAllCars => [],     // view recreation; one-shot
        SqlFeature.FreeWheels =>
        [
            "UPDATE List_Wheels SET price = (SELECT price FROM _backup_FreeWheels WHERE _backup_FreeWheels.Id = List_Wheels.Id) WHERE EXISTS (SELECT 1 FROM _backup_FreeWheels WHERE _backup_FreeWheels.Id = List_Wheels.Id);",
        ],
        SqlFeature.UnlockUpgradePresets =>
        [
            "UPDATE UpgradePresetPackages SET Purchasable = (SELECT Purchasable FROM _backup_UpgradePresets WHERE _backup_UpgradePresets.Id = UpgradePresetPackages.Id) WHERE EXISTS (SELECT 1 FROM _backup_UpgradePresets WHERE _backup_UpgradePresets.Id = UpgradePresetPackages.Id);",
        ],
        SqlFeature.FullAutoshow => [],   // view recreation; one-shot
        SqlFeature.DriftScoreScalar =>
        [
            "UPDATE Tracks SET DriftScoreScalar = (SELECT DriftScoreScalar FROM _backup_DriftScoreScalar WHERE _backup_DriftScoreScalar.rowid = Tracks.rowid) WHERE EXISTS (SELECT 1 FROM _backup_DriftScoreScalar WHERE _backup_DriftScoreScalar.rowid = Tracks.rowid);",
        ],
        SqlFeature.MaxTraction =>
        [
            "UPDATE Data_Car SET Traction_Road = (SELECT Traction_Road FROM _backup_MaxTraction_Car WHERE _backup_MaxTraction_Car.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_MaxTraction_Car WHERE _backup_MaxTraction_Car.Id = Data_Car.Id);",
            "UPDATE List_TireCompound SET WetFrictionModFrictionScale = (SELECT WetFrictionModFrictionScale FROM _backup_MaxTraction_Tire WHERE _backup_MaxTraction_Tire.rowid = List_TireCompound.rowid), WetOffroadFrictionScale = (SELECT WetOffroadFrictionScale FROM _backup_MaxTraction_Tire WHERE _backup_MaxTraction_Tire.rowid = List_TireCompound.rowid) WHERE EXISTS (SELECT 1 FROM _backup_MaxTraction_Tire WHERE _backup_MaxTraction_Tire.rowid = List_TireCompound.rowid);",
        ],
        SqlFeature.TorqueScale =>
        [
            "UPDATE Data_Car SET GameTorqueScale = (SELECT GameTorqueScale FROM _backup_TorqueScale WHERE _backup_TorqueScale.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_TorqueScale WHERE _backup_TorqueScale.Id = Data_Car.Id);",
        ],
        SqlFeature.DragScale =>
        [
            "UPDATE Data_Car SET GameDragScale = (SELECT GameDragScale FROM _backup_DragScale WHERE _backup_DragScale.Id = Data_Car.Id) WHERE EXISTS (SELECT 1 FROM _backup_DragScale WHERE _backup_DragScale.Id = Data_Car.Id);",
        ],
        _ => [],
    };

    /// <summary>
    /// All 47 upgrade tables that have a <c>price</c> column — 42 performance + 5 visual.
    /// From matkhl/FH6-DBDUMPER.
    /// </summary>
    internal static readonly string[] FreeUpgradeTables =
    [
        "List_UpgradeAntiSwayFront", "List_UpgradeAntiSwayRear", "List_UpgradeBrakes",
        "List_UpgradeCarBodyChassisStiffness", "List_UpgradeCarBody",
        "List_UpgradeCarBodyTireAspectRatioFront", "List_UpgradeCarBodyTireAspectRatioRear",
        "List_UpgradeCarBodyTireWidthFront", "List_UpgradeCarBodyTireWidthRear",
        "List_UpgradeCarBodyTrackSpacingFront", "List_UpgradeCarBodyTrackSpacingRear",
        "List_UpgradeCarBodyWeight", "List_UpgradeDrivetrain", "List_UpgradeDrivetrainClutch",
        "List_UpgradeDrivetrainDifferential", "List_UpgradeDrivetrainDriveline",
        "List_UpgradeDrivetrainTransmission", "List_UpgradeEngine", "List_UpgradeEngineCamshaft",
        "List_UpgradeEngineCSC", "List_UpgradeEngineDisplacement", "List_UpgradeEngineDSC",
        "List_UpgradeEngineExhaust", "List_UpgradeEngineFlywheel", "List_UpgradeEngineFuelSystem",
        "List_UpgradeEngineIgnition", "List_UpgradeEngineIntake", "List_UpgradeEngineIntercooler",
        "List_UpgradeEngineManifold", "List_UpgradeEngineOilCooling",
        "List_UpgradeEnginePistonsCompression", "List_UpgradeEngineRestrictorPlate",
        "List_UpgradeEngineTurboQuad", "List_UpgradeEngineTurboSingle", "List_UpgradeEngineTurboTwin",
        "List_UpgradeEngineValves", "List_UpgradeMotor", "List_UpgradeMotorParts",
        "List_UpgradeRimSizeFront", "List_UpgradeRimSizeRear", "List_UpgradeSpringDamper",
        "List_UpgradeTireCompound",
        "List_UpgradeCarBodyFrontBumper", "List_UpgradeCarBodyHood",
        "List_UpgradeCarBodyRearBumper", "List_UpgradeCarBodySideSkirt", "List_RearWing",
    ];
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class SeedSettingsFeatureKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BDD-015 Phase 1 — seed three new feature keys plus their plan-level
            // defaults. Uses ON CONFLICT DO NOTHING so running this after
            // DbInitializer (or vice versa) stays idempotent. FeatureCatalog.Id
            // mirrors the FeatureKey enum value:
            //   TableService       = 43
            //   ProviderPayments   = 100
            //   DeliveryPlatforms  = 110
            //
            // BusinessTypeFeature applicability rows are intentionally NOT seeded
            // here — MacroCategories 2/3/4 (QuickService, Retail, Services) are
            // populated by DbInitializer at app startup, so any FK-dependent
            // applicability row has to live there. DbInitializer.UpsertFeatureMatrixAsync
            // already carries the new entries (see POS.Repository/DbInitializer.cs).
            migrationBuilder.Sql("""
                INSERT INTO "FeatureCatalogs" ("Id", "Code", "Key", "Name", "Description", "IsQuantitative", "ResourceLabel", "SortOrder")
                VALUES
                    (43,  'TableService',      43,  'Servicio en mesa',              'Operación con mesas: órdenes sentados y gestión de estado de mesa',                         FALSE, NULL, 43),
                    (100, 'ProviderPayments',  100, 'Proveedores de pago externos',  'Integración con procesadores de pago (Clip, MercadoPago) y flujos de intent + webhook',     FALSE, NULL, 100),
                    (110, 'DeliveryPlatforms', 110, 'Plataformas de delivery',        'Integración con plataformas de reparto (UberEats, Rappi, DidiFood) con ingesta de webhooks', FALSE, NULL, 110)
                ON CONFLICT ("Id") DO NOTHING;

                -- PlanFeatureMatrix defaults (PlanTypeIds: Free=1, Basic=2, Pro=3, Enterprise=4)
                INSERT INTO "PlanFeatureMatrices" ("PlanTypeId", "FeatureId", "IsEnabled", "DefaultLimit")
                VALUES
                    (1, 43,  FALSE, NULL), (2, 43,  TRUE,  NULL), (3, 43,  TRUE, NULL), (4, 43,  TRUE, NULL),
                    (1, 100, FALSE, NULL), (2, 100, FALSE, NULL), (3, 100, TRUE, NULL), (4, 100, TRUE, NULL),
                    (1, 110, FALSE, NULL), (2, 110, FALSE, NULL), (3, 110, TRUE, NULL), (4, 110, TRUE, NULL)
                ON CONFLICT ("PlanTypeId", "FeatureId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "BusinessTypeFeatures" WHERE "FeatureId" IN (43, 100, 110);
                DELETE FROM "PlanFeatureMatrices"  WHERE "FeatureId" IN (43, 100, 110);
                DELETE FROM "FeatureCatalogs"      WHERE "Id"        IN (43, 100, 110);
                """);
        }
    }
}

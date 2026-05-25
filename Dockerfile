FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore (layer caching).
# Restore is scoped to POS.API and its transitive ProjectReferences
# (POS.Services → POS.Repository → POS.Domain). Test projects in the
# solution (POS.IntegrationTests, future POS.UnitTests) are intentionally
# NOT copied or restored here — they are not part of the production
# runtime graph and would force the image to carry test-only dependencies.
COPY POS.API/POS.API.csproj POS.API/
COPY POS.Domain/POS.Domain.csproj POS.Domain/
COPY POS.Repository/POS.Repository.csproj POS.Repository/
COPY POS.Services/POS.Services.csproj POS.Services/
RUN dotnet restore POS.API/POS.API.csproj

# Copy everything and publish. --no-restore reuses the layer above; if a
# csproj changed, the COPY layers above invalidate and trigger re-restore.
COPY . .
RUN dotnet publish POS.API/POS.API.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "POS.API.dll"]

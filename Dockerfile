FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore (layer caching)
COPY pos-api.slnx .
COPY POS.API/POS.API.csproj POS.API/
COPY POS.Domain/POS.Domain.csproj POS.Domain/
COPY POS.Repository/POS.Repository.csproj POS.Repository/
COPY POS.Services/POS.Services.csproj POS.Services/
RUN dotnet restore

# Copy everything and publish
COPY . .
RUN dotnet publish POS.API/POS.API.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "POS.API.dll"]

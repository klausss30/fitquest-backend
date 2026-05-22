FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FitQuest.Api.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish FitQuest.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "FitQuest.Api.dll"]

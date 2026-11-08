FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY StickerApp.Core/ StickerApp.Core/
COPY StickerApp.ShareServer/ StickerApp.ShareServer/
RUN dotnet publish StickerApp.ShareServer/StickerApp.ShareServer.csproj -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
USER root
RUN mkdir /data && chown app:app /data
COPY --from=build /publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Sharing__StoragePath=/data
ENV Friends__StoragePath=/data/friends
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "StickerApp.ShareServer.dll"]

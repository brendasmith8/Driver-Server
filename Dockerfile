#Stage 1
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /build
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /docker --no-restore

# Stage 2
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
ENV TZ=America/Los_Angeles
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone
WORKDIR /docker
COPY --from=build /docker .
ENTRYPOINT ["dotnet", "NightDriverServer.dll"]

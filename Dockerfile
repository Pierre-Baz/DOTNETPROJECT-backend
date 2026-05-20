FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY NetManage.Api.csproj ./
RUN dotnet restore NetManage.Api.csproj

COPY . ./
RUN dotnet publish NetManage.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish ./

EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet NetManage.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]

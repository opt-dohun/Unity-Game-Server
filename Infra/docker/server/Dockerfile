# 서버 이미지. 빌드 컨텍스트는 저장소 루트에서 지정한다.
#   docker build -t crimson-tide-server -f Infra/docker/server/Dockerfile .
#   docker run --rm -p 8080:8080 crimson-tide-server
#
# 주의: 프로젝트 디렉터리는 대소문자를 구분하는 Linux에서도 동작하도록
#       실제 경로와 동일한 "Server"를 사용한다.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 프로젝트 파일만 먼저 복사해 restore 결과를 캐시한다.
COPY Server/CrimsonTide.Server.csproj Server/
COPY Infra/CrimsonTide.Infra.csproj Infra/
RUN dotnet restore Server/CrimsonTide.Server.csproj

# 나머지 소스를 복사해 Release 게시물을 만든다.
COPY Server/ Server/
COPY Infra/ Infra/
RUN dotnet publish Server/CrimsonTide.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# 배포는 외부 MySQL을 사용한다. 연결 문자열은 CRIMSON_MYSQL_CONNECTION_STRING으로 secret 주입.
ENV BattleStore__Provider=mysql
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CrimsonTide.Server.dll"]

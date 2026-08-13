FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y socat && rm -rf /var/lib/apt/lists/*
COPY app/ ./
EXPOSE 8080
# The app hardcodes Kestrel to listen on 127.0.0.1:5080, so Docker port-publish and
# other containers can't reach it directly. socat bridges container eth0:8080 -> loopback:5080.
ENTRYPOINT ["/bin/bash","-c","socat TCP-LISTEN:8080,fork,reuseaddr TCP:127.0.0.1:5080 & exec dotnet BadmintonShop.dll"]

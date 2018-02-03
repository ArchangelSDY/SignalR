FROM ubuntu:xenial

RUN apt-get update && apt-get install -y vim netcat net-tools apt-utils git

RUN apt-get install -y unzip \
                    curl \
                    jq \
                    liblttng-ust0 \
                    libcurl3 \
                    libunwind8 \
                    libssl1.0.0 \
                    libuuid1 \
                    libkrb5-3 \
                    zlib1g \
                    libicu55

RUN curl -sL https://deb.nodesource.com/setup_8.x | bash - && \
    apt-get install -yq nodejs && \
    npm install npm -g

ENV REPO="https://github.com/VSChina/SignalR.git"
ENV BRANCH="master"

RUN git clone "${REPO}"
RUN cd SignalR && git checkout "${BRANCH}"
RUN /bin/bash /SignalR/build.sh ; exit 0
RUN /root/.dotnet/dotnet build /SignalR/samples/ServiceSample

EXPOSE 5001
CMD ["/root/.dotnet/dotnet", "run", "--no-build", "-p", "/SignalR/samples/ServiceSample"]

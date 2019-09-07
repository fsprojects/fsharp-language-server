FROM microsoft/dotnet:2.2-sdk

RUN curl -sL https://deb.nodesource.com/setup_12.x | bash -

RUN apt update && apt install -y \
        git \
        libunwind-dev \
        nodejs \
 && apt autoremove -y \
 && apt clean -y \
 && rm -rf /var/lib/apt/lists/*

ENV SDK_PATH=/usr/share/dotnet/sdk/2.2.401
RUN ln -s ${SDK_PATH}/Current ${SDK_PATH}/15.0

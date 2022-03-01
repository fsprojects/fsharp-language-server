#used to make paket use local package cache for github actions so we can cache that folder
sed '2 i storage:local' ./paket.dependencies
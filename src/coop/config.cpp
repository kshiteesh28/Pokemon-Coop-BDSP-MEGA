// Reads sd:/config/pokemon-coop.ini, the same key = value file the app writes:
//   address  = 100.67.109.85:4100
//   room     = AAROH
//   name     = Aaroh
//   password =

#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "nn/result.h"
#include "nn/fs.h"

#include "coop.hpp"

namespace coop {

namespace {
void trim(char* s) {
    size_t n = strlen(s);
    while (n && (isspace(static_cast<unsigned char>(s[n - 1])) || s[n - 1] == '"')) s[--n] = 0;
    size_t i = 0;
    while (s[i] && (isspace(static_cast<unsigned char>(s[i])) || s[i] == '"')) i++;
    if (i) memmove(s, s + i, strlen(s + i) + 1);
}

const char* gRoot = nullptr;

const char* sdRoot() {
    if (gRoot) return gRoot;
    nn::fs::DirectoryEntryType type;
    if (nn::Result(nn::fs::GetEntryType(&type, "sd:/")).isSuccess()) {
        gRoot = "sd:";
    } else {
        nn::fs::MountSdCardForDebug("coopsd");
        gRoot = "coopsd:";
    }
    return gRoot;
}

void copyField(char* dst, size_t cap, const char* src) {
    strncpy(dst, src, cap - 1);
    dst[cap - 1] = 0;
}
}  // namespace

void SdPath(char* out, size_t cap, const char* rel) {
    snprintf(out, cap, "%s/%s", sdRoot(), rel);
}

bool LoadSettings(Settings& out) {
    out = Settings{};
    nn::fs::FileHandle h{};
    char path[96];
    SdPath(path, sizeof(path), "config/pokemon-coop.ini");
    if (nn::Result(nn::fs::OpenFile(&h, path, nn::fs::OpenMode_Read)).isFailure()) {
        Log("No %s yet (press PLAY in the co-op app to create it)", path);
        return false;
    }
    long size = 0;
    nn::fs::GetFileSize(&size, h);
    static char buf[2048];
    if (size <= 0 || size >= static_cast<long>(sizeof(buf))) size = sizeof(buf) - 1;
    nn::fs::ReadFile(h, 0, buf, static_cast<ulong>(size));
    nn::fs::CloseFile(h);
    buf[size] = 0;

    char* next = buf;
    while (next && *next) {
        char* line = next;
        char* nl = strpbrk(line, "\r\n");
        if (nl) {
            *nl = 0;
            next = nl + 1;
        } else {
            next = nullptr;
        }
        if (static_cast<unsigned char>(line[0]) == 0xEF) line += 3;  // UTF-8 BOM
        trim(line);
        if (!line[0] || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
        char* eq = strchr(line, '=');
        if (!eq) continue;
        *eq = 0;
        char* key = line;
        char* val = eq + 1;
        trim(key);
        trim(val);
        for (char* k = key; *k; k++) *k = static_cast<char>(tolower(static_cast<unsigned char>(*k)));

        if (!strcmp(key, "address")) {
            char* colon = strrchr(val, ':');
            if (colon) {
                *colon = 0;
                int port = atoi(colon + 1);
                if (port > 0 && port < 65536) out.port = static_cast<uint16_t>(port);
            }
            copyField(out.host, sizeof(out.host), val);
        } else if (!strcmp(key, "room")) {
            copyField(out.room, sizeof(out.room), val);
        } else if (!strcmp(key, "name")) {
            copyField(out.name, sizeof(out.name), val);
        } else if (!strcmp(key, "password")) {
            copyField(out.password, sizeof(out.password), val);
        }
    }
    out.loaded = out.host[0] && out.name[0];
    if (!out.loaded) Log("pokemon-coop.ini is missing the address or name");
    return out.loaded;
}

}  // namespace coop

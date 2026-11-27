// Debug log and status file on the (virtual) SD card, so we can see what the
// co-op client is doing without a debugger:
//   sd:/config/pokemon-coop-log.txt     last ~64 log lines
//   sd:/config/pokemon-coop-status.txt  current connection state (rewritten every 2 s)
// In Ryujinx these are in %APPDATA%\Ryujinx\sdcard\config\.

#include <cstdarg>
#include <cstdio>
#include <cstring>

#include "nn/result.h"
#include "nn/fs.h"
#include "nn/os.h"

#include "coop.hpp"

namespace coop {

namespace {
constexpr int kLines = 64;
constexpr int kLineLen = 160;
char gLines[kLines][kLineLen];
int gHead = 0, gCount = 0;
nn::os::MutexType gLogMutex;
bool gLogMutexInit = false;

void writeWhole(const char* path, const char* data, size_t len) {
    nn::fs::DeleteFile(path);  // simplest way to truncate
    if (nn::Result(nn::fs::CreateFile(path, static_cast<s64>(len))).isFailure()) return;
    nn::fs::FileHandle h{};
    if (nn::Result(nn::fs::OpenFile(&h, path, nn::fs::OpenMode_Write)).isFailure()) return;
    nn::fs::WriteFile(h, 0, data, len, nn::fs::WriteOption::CreateOption(nn::fs::WriteOptionFlag_Flush));
    nn::fs::CloseFile(h);
}
}  // namespace

void Log(const char* fmt, ...) {
    if (!gLogMutexInit) {
        nn::os::InitializeMutex(&gLogMutex, false, 0);
        gLogMutexInit = true;
    }
    char line[kLineLen];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(line, sizeof(line), fmt, ap);
    va_end(ap);

    nn::os::LockMutex(&gLogMutex);
    strncpy(gLines[gHead], line, kLineLen - 1);
    gLines[gHead][kLineLen - 1] = 0;
    gHead = (gHead + 1) % kLines;
    if (gCount < kLines) gCount++;

    static char out[kLines * (kLineLen + 1)];
    size_t len = 0;
    for (int i = 0; i < gCount; i++) {
        const char* l = gLines[(gHead - gCount + i + kLines) % kLines];
        size_t n = strnlen(l, kLineLen);
        memcpy(out + len, l, n);
        len += n;
        out[len++] = '\n';
    }
    char path[96];
    SdPath(path, sizeof(path), "config/pokemon-coop-log.txt");
    writeWhole(path, out, len);
    nn::os::UnlockMutex(&gLogMutex);
}

void WriteStatusFile(const char* text) {
    char path[96];
    SdPath(path, sizeof(path), "config/pokemon-coop-status.txt");
    writeWhole(path, text, strlen(text));
}

}  // namespace coop

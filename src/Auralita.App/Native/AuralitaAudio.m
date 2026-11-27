#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>
#include <dispatch/dispatch.h>
#include <stdint.h>
#include <string.h>

@interface AuralitaPlayerHost : NSObject
@property(nonatomic, strong) AVAudioPlayer *player;
@end

@implementation AuralitaPlayerHost
@end

static AuralitaPlayerHost *gHost;
static char gLastError[1024];

static AuralitaPlayerHost *PlayerHost(void)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        gHost = [[AuralitaPlayerHost alloc] init];
    });
    return gHost;
}

static void SetLastError(NSString *message)
{
    const char *utf8Message = message.UTF8String;
    strlcpy(gLastError, utf8Message != NULL ? utf8Message : "Audio playback failed.", sizeof(gLastError));
}

int32_t auralita_play_file(const char *filePath)
{
    @autoreleasepool {
        if (filePath == NULL) {
            SetLastError(@"The selected audio file path is empty.");
            return 0;
        }

        NSString *path = [NSString stringWithUTF8String:filePath];
        if (path == nil) {
            SetLastError(@"The selected audio file path is invalid.");
            return 0;
        }

        NSError *error = nil;
        AVAudioPlayer *candidate = [[AVAudioPlayer alloc]
            initWithContentsOfURL:[NSURL fileURLWithPath:path]
                            error:&error];
        if (candidate == nil) {
            SetLastError(error.localizedDescription ?: @"macOS could not decode this audio file.");
            return 0;
        }

        if (![candidate prepareToPlay]) {
            SetLastError(@"macOS could not prepare this audio file for playback.");
            return 0;
        }

        AuralitaPlayerHost *host = PlayerHost();
        @synchronized(host) {
            [host.player stop];
            host.player = candidate;
            if (![host.player play]) {
                host.player = nil;
                SetLastError(@"macOS could not start audio output.");
                return 0;
            }
        }

        gLastError[0] = '\0';
        return 1;
    }
}

void auralita_stop(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        [host.player stop];
        host.player = nil;
    }
}

int32_t auralita_is_playing(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        return host.player.isPlaying ? 1 : 0;
    }
}

double auralita_current_time(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        return host.player != nil ? host.player.currentTime : 0;
    }
}

double auralita_duration(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        return host.player != nil ? host.player.duration : 0;
    }
}

const char *auralita_last_error(void)
{
    return gLastError;
}

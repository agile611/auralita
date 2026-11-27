#import <AVFoundation/AVFoundation.h>
#import <AppKit/AppKit.h>
#import <Foundation/Foundation.h>
#import <MediaPlayer/MediaPlayer.h>
#import <Security/Security.h>
#include <dispatch/dispatch.h>
#include <math.h>
#include <stdint.h>
#include <string.h>

@interface AuralitaPlayerHost : NSObject
@property(nonatomic, strong) AVAudioPlayer *player;
@property(nonatomic, strong) AVPlayer *streamPlayer;
@end

@implementation AuralitaPlayerHost
@end

static AuralitaPlayerHost *gHost;
static char gLastError[1024];
static char gCredentialBuffer[8192];

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
            [host.streamPlayer pause];
            host.streamPlayer = nil;
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

int32_t auralita_play_url(const char *mediaUrl)
{
    @autoreleasepool {
        if (mediaUrl == NULL) {
            SetLastError(@"The selected stream URL is empty.");
            return 0;
        }

        NSString *urlValue = [NSString stringWithUTF8String:mediaUrl];
        NSURL *url = urlValue != nil ? [NSURL URLWithString:urlValue] : nil;
        if (url == nil || ![@[@"http", @"https"] containsObject:url.scheme.lowercaseString]) {
            SetLastError(@"The selected stream URL must use HTTP or HTTPS.");
            return 0;
        }

        AuralitaPlayerHost *host = PlayerHost();
        @synchronized(host) {
            [host.player stop];
            host.player = nil;
            [host.streamPlayer pause];
            host.streamPlayer = [AVPlayer playerWithURL:url];
            if ([host.streamPlayer respondsToSelector:@selector(setAutomaticallyWaitsToMinimizeStalling:)]) {
                [host.streamPlayer setValue:@NO forKey:@"automaticallyWaitsToMinimizeStalling"];
            }
            [host.streamPlayer play];
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
        [host.streamPlayer pause];
        host.streamPlayer = nil;
    }
}

void auralita_pause(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        [host.player pause];
        [host.streamPlayer pause];
    }
}

void auralita_resume(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        [host.player play];
        [host.streamPlayer play];
    }
}

int32_t auralita_is_playing(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        if (host.streamPlayer.currentItem.status == AVPlayerItemStatusFailed) {
            SetLastError(@"The Plex stream could not be played.");
            return -1;
        }
        if (host.streamPlayer != nil) {
            AVPlayerItemStatus itemStatus = host.streamPlayer.currentItem.status;
            return host.streamPlayer.rate > 0 ||
                itemStatus != AVPlayerItemStatusReadyToPlay ||
                host.streamPlayer.timeControlStatus == AVPlayerTimeControlStatusWaitingToPlayAtSpecifiedRate
                ? 1
                : 0;
        }
        return host.player.isPlaying ? 1 : 0;
    }
}

double auralita_current_time(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        if (host.streamPlayer != nil) {
            double seconds = CMTimeGetSeconds(host.streamPlayer.currentTime);
            return isfinite(seconds) && seconds > 0 ? seconds : 0;
        }
        return host.player != nil ? host.player.currentTime : 0;
    }
}

double auralita_duration(void)
{
    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        if (host.streamPlayer.currentItem != nil) {
            double seconds = CMTimeGetSeconds(host.streamPlayer.currentItem.duration);
            return isfinite(seconds) && seconds > 0 ? seconds : 0;
        }
        return host.player != nil ? host.player.duration : 0;
    }
}

int32_t auralita_seek(double seconds)
{
    if (!isfinite(seconds) || seconds < 0) {
        return 0;
    }

    AuralitaPlayerHost *host = PlayerHost();
    @synchronized(host) {
        if (host.streamPlayer.currentItem != nil) {
            [host.streamPlayer seekToTime:CMTimeMakeWithSeconds(seconds, NSEC_PER_SEC)];
            return 1;
        }
        if (host.player != nil) {
            host.player.currentTime = MIN(seconds, host.player.duration);
            return 1;
        }
    }
    return 0;
}

const char *auralita_last_error(void)
{
    return gLastError;
}

int32_t auralita_keychain_set(const char *account, const char *secret)
{
    @autoreleasepool {
        if (account == NULL || secret == NULL) {
            return errSecParam;
        }

        NSString *accountValue = [NSString stringWithUTF8String:account];
        NSString *secretValue = [NSString stringWithUTF8String:secret];
        if (accountValue == nil || secretValue == nil) {
            return errSecParam;
        }

        NSDictionary *query = @{
            (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: @"com.agile611.auralita",
            (__bridge id)kSecAttrAccount: accountValue
        };
        NSData *data = [secretValue dataUsingEncoding:NSUTF8StringEncoding];
        OSStatus status = SecItemUpdate(
            (__bridge CFDictionaryRef)query,
            (__bridge CFDictionaryRef)@{(__bridge id)kSecValueData: data});
        if (status == errSecItemNotFound) {
            NSMutableDictionary *item = [query mutableCopy];
            item[(__bridge id)kSecValueData] = data;
            status = SecItemAdd((__bridge CFDictionaryRef)item, NULL);
        }
        return (int32_t)status;
    }
}

const char *auralita_keychain_get(const char *account, int32_t *status)
{
    @autoreleasepool {
        if (account == NULL || status == NULL) {
            return NULL;
        }

        NSString *accountValue = [NSString stringWithUTF8String:account];
        if (accountValue == nil) {
            *status = errSecParam;
            return NULL;
        }

        NSDictionary *query = @{
            (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: @"com.agile611.auralita",
            (__bridge id)kSecAttrAccount: accountValue,
            (__bridge id)kSecReturnData: @YES,
            (__bridge id)kSecMatchLimit: (__bridge id)kSecMatchLimitOne
        };
        CFTypeRef result = NULL;
        OSStatus resultStatus = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
        *status = (int32_t)resultStatus;
        memset(gCredentialBuffer, 0, sizeof(gCredentialBuffer));
        if (resultStatus != errSecSuccess) {
            return NULL;
        }

        NSData *data = CFBridgingRelease(result);
        if (data.length >= sizeof(gCredentialBuffer)) {
            *status = errSecParam;
            return NULL;
        }
        memcpy(gCredentialBuffer, data.bytes, data.length);
        gCredentialBuffer[data.length] = '\0';
        return gCredentialBuffer;
    }
}

int32_t auralita_keychain_item_not_found_status(void)
{
    return errSecItemNotFound;
}

int32_t auralita_keychain_delete(const char *account)
{
    @autoreleasepool {
        if (account == NULL) {
            return errSecParam;
        }

        NSString *accountValue = [NSString stringWithUTF8String:account];
        if (accountValue == nil) {
            return errSecParam;
        }

        NSDictionary *query = @{
            (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService: @"com.agile611.auralita",
            (__bridge id)kSecAttrAccount: accountValue
        };
        return (int32_t)SecItemDelete((__bridge CFDictionaryRef)query);
    }
}

typedef void (*AuralitaCommandHandler)(int command);
typedef void (*AuralitaSeekHandler)(double seconds);

static AuralitaCommandHandler gCommandHandler;
static AuralitaSeekHandler gSeekHandler;
static NSImage *gArtwork;

static void AuralitaHandleCommand(int command)
{
    if (gCommandHandler != NULL) {
        gCommandHandler(command);
    }
}

void auralita_media_session_set_handlers(AuralitaCommandHandler commandHandler, AuralitaSeekHandler seekHandler)
{
    gCommandHandler = commandHandler;
    gSeekHandler = seekHandler;

    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        MPRemoteCommandCenter *center = [MPRemoteCommandCenter sharedCommandCenter];
        center.playCommand.enabled = YES;
        [center.playCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            AuralitaHandleCommand(0);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
        center.pauseCommand.enabled = YES;
        [center.pauseCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            AuralitaHandleCommand(1);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
        center.togglePlayPauseCommand.enabled = YES;
        [center.togglePlayPauseCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            AuralitaHandleCommand(2);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
        center.nextTrackCommand.enabled = YES;
        [center.nextTrackCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            AuralitaHandleCommand(3);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
        center.previousTrackCommand.enabled = YES;
        [center.previousTrackCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            AuralitaHandleCommand(4);
            return MPRemoteCommandHandlerStatusSuccess;
        }];
        center.changePlaybackPositionCommand.enabled = YES;
        [center.changePlaybackPositionCommand addTargetWithHandler:^(MPRemoteCommandEvent *event) {
            if (gSeekHandler != NULL) {
                gSeekHandler([(MPChangePlaybackPositionCommandEvent *)event positionTime]);
            }
            return MPRemoteCommandHandlerStatusSuccess;
        }];
    });
}

void auralita_media_session_update_now_playing(
    const char *title,
    const char *artist,
    const char *album,
    double durationSeconds,
    double elapsedSeconds,
    int state)
{
    @autoreleasepool {
        MPNowPlayingInfoCenter *center = [MPNowPlayingInfoCenter defaultCenter];
        if (state == 0) {
            center.nowPlayingInfo = nil;
            return;
        }

        NSMutableDictionary *info = [NSMutableDictionary dictionary];
        if (title != NULL) {
            info[MPMediaItemPropertyTitle] = [NSString stringWithUTF8String:title];
        }
        if (artist != NULL) {
            info[MPMediaItemPropertyArtist] = [NSString stringWithUTF8String:artist];
        }
        if (album != NULL) {
            info[MPMediaItemPropertyAlbumTitle] = [NSString stringWithUTF8String:album];
        }
        if (isfinite(durationSeconds) && durationSeconds > 0) {
            info[MPMediaItemPropertyPlaybackDuration] = @(durationSeconds);
        }
        if (isfinite(elapsedSeconds) && elapsedSeconds >= 0) {
            info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(elapsedSeconds);
        }
        info[MPNowPlayingInfoPropertyPlaybackRate] = state == 1 ? @1.0 : @0.0;
        if (gArtwork != nil) {
            CGSize bounds = gArtwork.size;
            MPMediaItemArtwork *artwork = [[MPMediaItemArtwork alloc] initWithBoundsSize:bounds requestHandler:^NSImage *(CGSize requestedSize) {
                NSImage *image = [[NSImage alloc] initWithSize:requestedSize];
                [image lockFocus];
                [gArtwork drawInRect:NSMakeRect(0, 0, requestedSize.width, requestedSize.height)
                            fromRect:NSZeroRect
                           operation:NSCompositingOperationCopy
                            fraction:1.0];
                [image unlockFocus];
                return image;
            }];
            info[MPMediaItemPropertyArtwork] = artwork;
        }
        center.nowPlayingInfo = info;
    }
}

void auralita_media_session_set_artwork(const void *bytes, int32_t length)
{
    @autoreleasepool {
        if (bytes == NULL || length <= 0) {
            gArtwork = nil;
            return;
        }

        NSData *data = [NSData dataWithBytes:bytes length:(NSUInteger)length];
        gArtwork = [[NSImage alloc] initWithData:data];
    }
}

void auralita_media_session_clear_artwork(void)
{
    gArtwork = nil;
}

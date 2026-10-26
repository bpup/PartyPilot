#ifndef PARTYPILOT_PRIVATE_SLOTS_H
#define PARTYPILOT_PRIVATE_SLOTS_H

#include <stddef.h>
#include <stdint.h>

#define PP_FRAME_SIZE 76u
#define PP_CONFIG_SIZE 20u
#define PP_MAGIC 0x50505336u
#define PP_VERSION 1u
#define PP_CONFIG_MAGIC 0x43535050u
#define PP_NO_INDEX 0xffffffffu
#define PP_STATUS_STARTING 1u
#define PP_STATUS_READY 2u
#define PP_STATUS_FAILED 3u
#define PP_STATUS_STOPPING 5u
#define PP_SLOT_P5 1u
#define PP_SLOT_P6 2u

typedef struct pp_report {
    uint32_t packet;
    uint16_t buttons;
    uint8_t left_trigger;
    uint8_t right_trigger;
    int16_t left_x;
    int16_t left_y;
    int16_t right_x;
    int16_t right_y;
    uint64_t sample_tick;
} pp_report;

// Unavailable means the slot must appear disconnected. Neutral means the slot
// remains connected but a stale report must be released to zero input.
typedef enum pp_read_result {
    PP_UNAVAILABLE = 0,
    PP_NEUTRAL = 1,
    PP_FRESH = 2
} pp_read_result;

pp_read_result pp_read_pad(const volatile uint8_t *frame, size_t length,
                           int player, uint64_t now_tick, uint64_t max_age_ms,
                           pp_report *out);

int pp_read_config(const volatile uint8_t *config, size_t length,
                   uint32_t connected, uint32_t *p5_index, uint32_t *p6_index);

#endif

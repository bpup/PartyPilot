#include "private_slots.h"

#include <stdatomic.h>
#include <string.h>

static uint16_t read16(const volatile uint8_t *data) {
    return (uint16_t)data[0] | ((uint16_t)data[1] << 8);
}

static uint32_t read32(const volatile uint8_t *data) {
    return (uint32_t)read16(data) | ((uint32_t)read16(data + 2) << 16);
}

static uint64_t read64(const volatile uint8_t *data) {
    return (uint64_t)read32(data) | ((uint64_t)read32(data + 4) << 32);
}

pp_read_result pp_read_pad(const volatile uint8_t *frame, size_t length,
                           int player, uint64_t now_tick, uint64_t max_age_ms,
                           pp_report *out) {
    if (out == NULL) return PP_UNAVAILABLE;
    memset(out, 0, sizeof(*out));
    if (frame == NULL || length != PP_FRAME_SIZE || (player != 5 && player != 6) ||
        read32(frame) != PP_MAGIC || read32(frame + 4) != PP_VERSION ||
        read32(frame + 8) != PP_FRAME_SIZE || read32(frame + 12) != PP_STATUS_READY)
        return PP_UNAVAILABLE;

    uint32_t connected = read32(frame + 16);
    if (connected != PP_SLOT_P5 && connected != (PP_SLOT_P5 | PP_SLOT_P6))
        return PP_UNAVAILABLE;
    if ((connected & (player == 5 ? PP_SLOT_P5 : PP_SLOT_P6)) == 0)
        return PP_UNAVAILABLE;

    const volatile uint8_t *slot = frame + 20 + (player - 5) * 28;
    uint32_t sequence = read32(slot);
    if (sequence == 0 || (sequence & 1u) != 0) return PP_UNAVAILABLE;
    atomic_thread_fence(memory_order_acquire);
    pp_report candidate;
    candidate.packet = read32(slot + 4);
    candidate.buttons = read16(slot + 8);
    candidate.left_trigger = slot[10];
    candidate.right_trigger = slot[11];
    candidate.left_x = (int16_t)read16(slot + 12);
    candidate.left_y = (int16_t)read16(slot + 14);
    candidate.right_x = (int16_t)read16(slot + 16);
    candidate.right_y = (int16_t)read16(slot + 18);
    candidate.sample_tick = read64(slot + 20);
    atomic_thread_fence(memory_order_acquire);
    if (sequence != read32(slot) || read32(frame + 12) != PP_STATUS_READY)
        return PP_UNAVAILABLE;

    if (candidate.sample_tick > now_tick ||
        now_tick - candidate.sample_tick > max_age_ms) {
        out->packet = candidate.packet + 1;
        return PP_NEUTRAL;
    }
    *out = candidate;
    return PP_FRESH;
}

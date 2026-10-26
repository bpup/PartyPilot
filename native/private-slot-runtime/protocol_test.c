#include "private_slots.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>

static void put16(uint8_t *p, uint16_t v) { p[0] = (uint8_t)v; p[1] = (uint8_t)(v >> 8); }
static void put32(uint8_t *p, uint32_t v) { put16(p, (uint16_t)v); put16(p + 2, (uint16_t)(v >> 16)); }
static void put64(uint8_t *p, uint64_t v) { put32(p, (uint32_t)v); put32(p + 4, (uint32_t)(v >> 32)); }

int main(void) {
    uint8_t frame[PP_FRAME_SIZE] = {0};
    pp_report output;
    put32(frame, PP_MAGIC);
    put32(frame + 4, PP_VERSION);
    put32(frame + 8, PP_FRAME_SIZE);
    put32(frame + 12, PP_STATUS_READY);
    put32(frame + 16, PP_SLOT_P5 | PP_SLOT_P6);
    put32(frame + 20, 2);
    put32(frame + 24, 17);
    put16(frame + 28, 0x1000);
    frame[30] = 25;
    frame[31] = 50;
    put16(frame + 32, (uint16_t)-123);
    put16(frame + 34, 456);
    put64(frame + 40, 1000);
    put32(frame + 48, 2);
    put32(frame + 52, 18);
    put16(frame + 56, 0x0020);
    put64(frame + 68, 1001);

    assert(pp_read_pad(frame, sizeof(frame), 5, 1100, 500, &output) == PP_FRESH);
    assert(output.packet == 17 && output.buttons == 0x1000 &&
           output.left_trigger == 25 && output.right_trigger == 50 &&
           output.left_x == -123 && output.left_y == 456);
    assert(pp_read_pad(frame, sizeof(frame), 6, 1100, 500, &output) == PP_FRESH);
    assert(output.packet == 18 && output.buttons == 0x0020);
    assert(pp_read_pad(frame, sizeof(frame), 5, 1501, 500, &output) == PP_NEUTRAL);
    assert(output.buttons == 0 && output.packet == 18);
    frame[20] = 3;
    assert(pp_read_pad(frame, sizeof(frame), 5, 1100, 500, &output) == PP_UNAVAILABLE);
    frame[20] = 2;
    put32(frame + 16, PP_SLOT_P5);
    assert(pp_read_pad(frame, sizeof(frame), 6, 1100, 500, &output) == PP_UNAVAILABLE);
    put32(frame + 12, 5);
    assert(pp_read_pad(frame, sizeof(frame), 5, 1100, 500, &output) == PP_UNAVAILABLE);
    puts("Private slot protocol checks passed.");
    return 0;
}

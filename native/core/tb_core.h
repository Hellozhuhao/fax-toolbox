#ifndef TB_CORE_H
#define TB_CORE_H

#include <stdint.h>

#if defined(_WIN32)
#if defined(TB_CORE_BUILD)
#define TB_API __declspec(dllexport)
#else
#define TB_API __declspec(dllimport)
#endif
#else
#define TB_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct tb_core tb_core;
typedef enum tb_event {
  TB_PAGE_READY = 1,
  TB_VIDEO_FOUND = 2,
  TB_VIDEO_ENDED = 3,
  TB_CANCEL = 4
} tb_event;
typedef enum tb_state {
  TB_CREATED = 0,
  TB_READY = 1,
  TB_OBSERVING = 2,
  TB_COMPLETE = 3,
  TB_CANCELLED = 4
} tb_state;
typedef enum tb_result {
  TB_OK = 0,
  TB_DUPLICATE = 1,
  TB_INVALID = -1
} tb_result;

TB_API tb_core* tb_core_create(void);
TB_API void tb_core_destroy(tb_core* core);
TB_API tb_result tb_core_dispatch(tb_core* core, tb_event event, uint64_t sequence);
TB_API tb_state tb_core_state(const tb_core* core);
TB_API uint64_t tb_core_last_sequence(const tb_core* core);

#ifdef __cplusplus
}
#endif
#endif

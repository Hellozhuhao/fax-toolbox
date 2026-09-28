#include "tb_core.h"
#include <new>

struct tb_core {
  tb_state state = TB_CREATED;
  uint64_t last_sequence = 0;
};

extern "C" TB_API tb_core* tb_core_create(void) {
  return new (std::nothrow) tb_core();
}

extern "C" TB_API void tb_core_destroy(tb_core* core) {
  delete core;
}

extern "C" TB_API tb_result tb_core_dispatch(tb_core* core, tb_event event, uint64_t sequence) {
  if (!core || sequence == 0) return TB_INVALID;
  if (sequence <= core->last_sequence) return TB_DUPLICATE;
  tb_state next = core->state;
  if (event == TB_CANCEL && next != TB_COMPLETE && next != TB_CANCELLED) {
    next = TB_CANCELLED;
  } else if (next == TB_CREATED && event == TB_PAGE_READY) {
    next = TB_READY;
  } else if (next == TB_READY && event == TB_VIDEO_FOUND) {
    next = TB_OBSERVING;
  } else if (next == TB_OBSERVING && event == TB_VIDEO_ENDED) {
    next = TB_COMPLETE;
  } else {
    return TB_INVALID;
  }
  core->state = next;
  core->last_sequence = sequence;
  return TB_OK;
}

extern "C" TB_API tb_state tb_core_state(const tb_core* core) {
  return core ? core->state : TB_CREATED;
}

extern "C" TB_API uint64_t tb_core_last_sequence(const tb_core* core) {
  return core ? core->last_sequence : 0;
}

#include "tb_core.h"
#include <cassert>
#include <cstdio>

int main() {
  tb_core* core = tb_core_create();
  assert(core != nullptr);
  assert(tb_core_state(core) == TB_CREATED);
  assert(tb_core_dispatch(core, TB_VIDEO_FOUND, 1) == TB_INVALID);
  assert(tb_core_dispatch(core, TB_PAGE_READY, 1) == TB_OK);
  assert(tb_core_dispatch(core, TB_PAGE_READY, 1) == TB_DUPLICATE);
  assert(tb_core_dispatch(core, TB_VIDEO_FOUND, 2) == TB_OK);
  assert(tb_core_dispatch(core, TB_VIDEO_ENDED, 3) == TB_OK);
  assert(tb_core_state(core) == TB_COMPLETE);
  assert(tb_core_dispatch(core, TB_CANCEL, 4) == TB_INVALID);
  assert(tb_core_last_sequence(core) == 3);
  tb_core_destroy(core);
  std::puts("PASS native C ABI state core");
  return 0;
}

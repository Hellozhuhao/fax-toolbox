#include <jni.h>
#include <stdint.h>
#include "tb_core.h"

extern "C" JNIEXPORT jlong JNICALL
Java_com_example_toolbox_MainActivity_nativeCreate(JNIEnv*, jclass) {
  return reinterpret_cast<jlong>(tb_core_create());
}

extern "C" JNIEXPORT void JNICALL
Java_com_example_toolbox_MainActivity_nativeDestroy(JNIEnv*, jclass, jlong handle) {
  tb_core_destroy(reinterpret_cast<tb_core*>(handle));
}

extern "C" JNIEXPORT jint JNICALL
Java_com_example_toolbox_MainActivity_nativeDispatch(JNIEnv*, jclass,
    jlong handle, jint event, jlong sequence) {
  auto* core = reinterpret_cast<tb_core*>(handle);
  if (!core || sequence <= 0) return -1;
  tb_result result = tb_core_dispatch(core, static_cast<tb_event>(event),
                                      static_cast<uint64_t>(sequence));
  return result == TB_OK ? static_cast<jint>(tb_core_state(core)) : -1;
}

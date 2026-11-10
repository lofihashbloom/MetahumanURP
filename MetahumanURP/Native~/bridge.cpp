#include <riglogic/RigLogic.h>
#include <dna/BinaryStreamReader.h>
#include <trio/streams/MemoryStream.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <exception>
#include <memory>
#include <stdexcept>

#if defined(_WIN32)
#define MH_API extern "C" __declspec(dllexport)
#else
#define MH_API extern "C" __attribute__((visibility("default")))
#endif

namespace {
void message(char* output, int capacity, const char* text) {
    if (!output || capacity <= 0) return;
    std::strncpy(output, text, static_cast<size_t>(capacity - 1));
    output[capacity - 1] = '\0';
}
struct Rig {
    decltype(rl4::makeScoped<rl4::MemoryStream>()) stream = rl4::makeScoped<rl4::MemoryStream>();
    decltype(rl4::makeScoped<rl4::BinaryStreamReader>(stream.get())) reader;
    decltype(rl4::makeScoped<rl4::RigLogic>(reader.get())) logic;
    decltype(rl4::makeScoped<rl4::RigInstance>(logic.get())) instance;
    Rig(const unsigned char* bytes, int size) {
        stream->write(reinterpret_cast<const char*>(bytes), static_cast<size_t>(size));
        stream->seek(0);
        reader = rl4::makeScoped<rl4::BinaryStreamReader>(stream.get());
        reader->read();
        if (!rl4::Status::isOk()) throw std::runtime_error(rl4::Status::get().message);
        if (reader->getJointCount() == 0 || reader->getRawControlCount() == 0)
            throw std::runtime_error("DNA contains no joint behavior or raw controls.");
        rl4::Configuration config;
        config.rotationType = rl4::RotationType::Quaternions;
        config.floatingPointType = rl4::FloatingPointType::Float;
        logic = rl4::makeScoped<rl4::RigLogic>(reader.get(), config);
        instance = rl4::makeScoped<rl4::RigInstance>(logic.get());
        instance->setLOD(0);
        logic->calculate(instance.get());
    }
};
template<typename View> void copy(View data, float* output) {
    if (data.size()) std::copy(data.begin(), data.end(), output);
}
}

MH_API int mh_abi_version() { return 1; }
MH_API void* mh_create(const unsigned char* bytes, int size, char* error, int capacity) {
    message(error, capacity, "");
    if (!bytes || size < 7) { message(error, capacity, "DNA bytes are missing or truncated."); return nullptr; }
    try { return new Rig(bytes, size); }
    catch (const std::exception& ex) { message(error, capacity, ex.what()); }
    catch (...) { message(error, capacity, "Unknown OpenRigLogic error."); }
    return nullptr;
}
MH_API void mh_destroy(void* handle) { delete static_cast<Rig*>(handle); }
// Counts: raw, joints, joint values, morph channels, animated maps, LODs, GUI controls.
MH_API int mh_counts(void* handle, int* output, int capacity) {
    if (!handle || !output || capacity < 7) return 0;
    auto& rig = *static_cast<Rig*>(handle);
    output[0] = rig.reader->getRawControlCount();
    output[1] = rig.reader->getJointCount();
    output[2] = static_cast<int>(rig.instance->getJointOutputs().size());
    output[3] = static_cast<int>(rig.instance->getBlendShapeOutputs().size());
    output[4] = static_cast<int>(rig.instance->getAnimatedMapOutputs().size());
    output[5] = rig.logic->getLODCount();
    output[6] = rig.reader->getGUIControlCount();
    return 1;
}
// Name kinds: raw control, joint, morph channel, animated map, GUI control.
MH_API int mh_name(void* handle, int kind, int index, char* output, int capacity) {
    if (!handle || index < 0 || !output || capacity <= 0) return 0;
    auto& reader = static_cast<Rig*>(handle)->reader;
    rl4::StringView name;
    switch (kind) {
        case 0: if (index >= reader->getRawControlCount()) return 0; name = reader->getRawControlName(index); break;
        case 1: if (index >= reader->getJointCount()) return 0; name = reader->getJointName(index); break;
        case 2: if (index >= reader->getBlendShapeChannelCount()) return 0; name = reader->getBlendShapeChannelName(index); break;
        case 3: if (index >= reader->getAnimatedMapCount()) return 0; name = reader->getAnimatedMapName(index); break;
        case 4: if (index >= reader->getGUIControlCount()) return 0; name = reader->getGUIControlName(index); break;
        default: return 0;
    }
    if (static_cast<int>(name.size()) >= capacity) return 0;
    std::memcpy(output, name.data(), name.size()); output[name.size()] = '\0';
    return 1;
}
MH_API int mh_neutral(void* handle, float* output, int capacity) {
    if (!handle || !output) return 0;
    auto values = static_cast<Rig*>(handle)->logic->getNeutralJointValues();
    if (capacity < static_cast<int>(values.size())) return 0;
    copy(values, output); return 1;
}
MH_API int mh_parent(void* handle, int index) {
    if (!handle || index < 0 || index >= static_cast<Rig*>(handle)->reader->getJointCount()) return -1;
    return static_cast<Rig*>(handle)->reader->getJointParentIndex(index);
}
MH_API int mh_evaluate(void* handle, const float* raw, int rawCount, int lod,
                       float* joints, int jointCount, float* morphs, int morphCount,
                       float* maps, int mapCount) {
    if (!handle || !raw || !joints) return 0;
    auto& rig = *static_cast<Rig*>(handle);
    if (rawCount != rig.reader->getRawControlCount() || lod < 0 || lod >= rig.logic->getLODCount()
        || jointCount != static_cast<int>(rig.instance->getJointOutputs().size())
        || morphCount != static_cast<int>(rig.instance->getBlendShapeOutputs().size())
        || mapCount != static_cast<int>(rig.instance->getAnimatedMapOutputs().size())
        || (morphCount && !morphs) || (mapCount && !maps)) return 0;
    for (int i = 0; i < rawCount; ++i) if (!std::isfinite(raw[i])) return 0;
    try {
        rig.instance->setLOD(static_cast<uint16_t>(lod));
        for (int i = 0; i < rawCount; ++i) rig.instance->setRawControl(static_cast<uint16_t>(i), raw[i]);
        rig.logic->calculate(rig.instance.get());
        copy(rig.instance->getJointOutputs(), joints);
        copy(rig.instance->getBlendShapeOutputs(), morphs);
        copy(rig.instance->getAnimatedMapOutputs(), maps);
        return 1;
    } catch (...) { return 0; }
}

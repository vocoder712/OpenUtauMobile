#ifndef WORLDLINE_PHRASE_SYNTH_H_
#define WORLDLINE_PHRASE_SYNTH_H_

namespace worldline {

using LogCallback = void(*)(const char*);

class PhraseSynth {
 public:
  PhraseSynth() = default;
  ~PhraseSynth() = default;
};

}  // namespace worldline

#endif  // WORLDLINE_PHRASE_SYNTH_H_

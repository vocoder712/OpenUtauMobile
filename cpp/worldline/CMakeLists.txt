cmake_minimum_required(VERSION 3.20)
project(worldline LANGUAGES C CXX)

# Basic settings
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Options for optional deps (disabled by default for iOS)
option(ENABLE_PYIN "Enable optional pyin estimator (requires libpyin)" OFF)
option(ENABLE_MINIAUDIO "Enable miniaudio support" OFF)

include(FetchContent)
# Prefer a vendored copy in known locations if present (repo root or module dir)
set(_WORLD_VENDOR_DIRS
  "${CMAKE_SOURCE_DIR}/third_party/world"
  "${CMAKE_CURRENT_SOURCE_DIR}/third_party/world"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/world"
)
set(_WORLD_VENDORED_USED FALSE)
foreach(_d IN LISTS _WORLD_VENDOR_DIRS)
  if(EXISTS "${_d}/CMakeLists.txt")
    message(STATUS "Using vendored WORLD at ${_d}")
    add_subdirectory("${_d}" "${CMAKE_BINARY_DIR}/_deps/world-build")
    set(_WORLD_VENDORED_USED TRUE)
    break()
  endif()
endforeach()

if(NOT _WORLD_VENDORED_USED)
  # Prefer an existing _deps/world-src if present; otherwise allow FetchContent to pull WORLD
  if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/build_xcode/_deps/world-src/CMakeLists.txt")
    add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/build_xcode/_deps/world-src" "${CMAKE_BINARY_DIR}/_deps/world-build")
  else()
    FetchContent_Declare(
      WORLD
      GIT_REPOSITORY https://github.com/mmorise/World.git
      GIT_TAG master
    )
    FetchContent_MakeAvailable(WORLD)
  endif()
endif()

# Collect sources (exclude build directories)
file(GLOB_RECURSE WORLDLINE_ALL_SRCS CONFIGURE_DEPENDS
  "${CMAKE_CURRENT_SOURCE_DIR}/*.c"
  "${CMAKE_CURRENT_SOURCE_DIR}/*.cpp"
  "${CMAKE_CURRENT_SOURCE_DIR}/*.cc"
)

# Filter out generated/build directories
set(WORLDLINE_SRCS "")
foreach(f ${WORLDLINE_ALL_SRCS})
  string(FIND "${f}" "/build_xcode/" idx)
  if(idx EQUAL -1)
    list(APPEND WORLDLINE_SRCS "${f}")
  endif()
endforeach()

# Minimal library
add_library(worldline SHARED ${WORLDLINE_SRCS})
add_library(worldline::worldline ALIAS worldline)

target_include_directories(worldline PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

target_compile_definitions(worldline PRIVATE "WORLDLINE_STATIC=1")

# Link to WORLD
find_library(WORLD_LIB NAMES WORLD PATHS ${CMAKE_BINARY_DIR}/_deps/world-build ${CMAKE_CURRENT_SOURCE_DIR}/build_xcode/_deps/world-build NO_DEFAULT_PATH)
if(WORLD_LIB)
  target_link_libraries(worldline PRIVATE ${WORLD_LIB})
else()
  # If add_subdirectory/fetch produced a target, link it
  if(TARGET WORLD::WORLD)
    target_link_libraries(worldline PRIVATE WORLD::WORLD)
  endif()
endif()

# Optional deps guarded
if(ENABLE_PYIN)
  target_compile_definitions(worldline PRIVATE ENABLE_PYIN=1)
endif()

# Export header (worldline.h expected in source root)
install(TARGETS worldline
  LIBRARY DESTINATION lib
  ARCHIVE DESTINATION lib
)
install(FILES worldline.h DESTINATION include OPTIONAL)

# Allow 'make install' style
include(GNUInstallDirs)

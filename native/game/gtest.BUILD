load("@rules_cc//cc:cc_library.bzl", "cc_library")
package(default_visibility = ["//visibility:public"])
config_setting(name = "windows", constraint_values = ["@platforms//os:windows"])
cc_library(
    name = "gtest",
    testonly = True,
    srcs = glob(["googletest/src/*.cc", "googletest/src/*.h"], exclude = ["googletest/src/gtest-all.cc", "googletest/src/gtest_main.cc"]),
    hdrs = glob(["googletest/include/**/*.h"]),
    includes = ["googletest", "googletest/include"],
    linkopts = select({":windows": [], "//conditions:default": ["-pthread"]}),
)
cc_library(name = "gtest_main", testonly = True, srcs = ["googletest/src/gtest_main.cc"], deps = [":gtest"])

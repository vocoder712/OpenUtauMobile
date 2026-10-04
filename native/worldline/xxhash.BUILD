load("@rules_cc//cc:cc_library.bzl", "cc_library")

filegroup(name = "opum_license", srcs = ["LICENSE"], visibility = ["//visibility:public"])

cc_library(
    name = "xxhash",
    srcs = ["xxhash.c"],
    hdrs = ["xxh3.h", "xxhash.h"],
    visibility = ["//visibility:public"],
)

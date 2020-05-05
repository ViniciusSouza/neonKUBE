// The MIT License (MIT)
//
// Copyright (c) 2020 Temporal Technologies, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Code generated by protoc-gen-gogo. DO NOT EDIT.
// source: namespace/enum.proto

package namespace

import (
	fmt "fmt"
	proto "github.com/gogo/protobuf/proto"
	math "math"
)

// Reference imports to suppress errors if they are not otherwise used.
var _ = proto.Marshal
var _ = fmt.Errorf
var _ = math.Inf

// This is a compile-time assertion to ensure that this generated file
// is compatible with the proto package it is being compiled against.
// A compilation error at this line likely means your copy of the
// proto package needs to be updated.
const _ = proto.GoGoProtoPackageIsVersion3 // please upgrade the proto package

type NamespaceStatus int32

const (
	NamespaceStatus_Registered NamespaceStatus = 0
	NamespaceStatus_Deprecated NamespaceStatus = 1
	NamespaceStatus_Deleted    NamespaceStatus = 2
)

var NamespaceStatus_name = map[int32]string{
	0: "Registered",
	1: "Deprecated",
	2: "Deleted",
}

var NamespaceStatus_value = map[string]int32{
	"Registered": 0,
	"Deprecated": 1,
	"Deleted":    2,
}

func (x NamespaceStatus) String() string {
	return proto.EnumName(NamespaceStatus_name, int32(x))
}

func (NamespaceStatus) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_99191c63d57e747f, []int{0}
}

type ArchivalStatus int32

const (
	ArchivalStatus_Default  ArchivalStatus = 0
	ArchivalStatus_Disabled ArchivalStatus = 1
	ArchivalStatus_Enabled  ArchivalStatus = 2
)

var ArchivalStatus_name = map[int32]string{
	0: "Default",
	1: "Disabled",
	2: "Enabled",
}

var ArchivalStatus_value = map[string]int32{
	"Default":  0,
	"Disabled": 1,
	"Enabled":  2,
}

func (x ArchivalStatus) String() string {
	return proto.EnumName(ArchivalStatus_name, int32(x))
}

func (ArchivalStatus) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_99191c63d57e747f, []int{1}
}

func init() {
	proto.RegisterEnum("namespace.NamespaceStatus", NamespaceStatus_name, NamespaceStatus_value)
	proto.RegisterEnum("namespace.ArchivalStatus", ArchivalStatus_name, ArchivalStatus_value)
}

func init() { proto.RegisterFile("namespace/enum.proto", fileDescriptor_99191c63d57e747f) }

var fileDescriptor_99191c63d57e747f = []byte{
	// 211 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0xe2, 0x12, 0xc9, 0x4b, 0xcc, 0x4d,
	0x2d, 0x2e, 0x48, 0x4c, 0x4e, 0xd5, 0x4f, 0xcd, 0x2b, 0xcd, 0xd5, 0x2b, 0x28, 0xca, 0x2f, 0xc9,
	0x17, 0xe2, 0x84, 0x8b, 0x6a, 0xd9, 0x71, 0xf1, 0xfb, 0xc1, 0x38, 0xc1, 0x25, 0x89, 0x25, 0xa5,
	0xc5, 0x42, 0x7c, 0x5c, 0x5c, 0x41, 0xa9, 0xe9, 0x99, 0xc5, 0x25, 0xa9, 0x45, 0xa9, 0x29, 0x02,
	0x0c, 0x20, 0xbe, 0x4b, 0x6a, 0x41, 0x51, 0x6a, 0x72, 0x62, 0x49, 0x6a, 0x8a, 0x00, 0xa3, 0x10,
	0x37, 0x17, 0xbb, 0x4b, 0x6a, 0x4e, 0x2a, 0x88, 0xc3, 0xa4, 0x65, 0xc1, 0xc5, 0xe7, 0x58, 0x94,
	0x9c, 0x91, 0x59, 0x96, 0x98, 0x03, 0xd5, 0x0e, 0x96, 0x4e, 0x4b, 0x2c, 0xcd, 0x29, 0x11, 0x60,
	0x10, 0xe2, 0xe1, 0xe2, 0x70, 0xc9, 0x2c, 0x4e, 0x4c, 0xca, 0x81, 0xe9, 0x74, 0xcd, 0x83, 0x70,
	0x98, 0x9c, 0x12, 0x4e, 0x3c, 0x92, 0x63, 0xbc, 0xf0, 0x48, 0x8e, 0xf1, 0xc1, 0x23, 0x39, 0xc6,
	0x09, 0x8f, 0xe5, 0x18, 0x2e, 0x3c, 0x96, 0x63, 0xb8, 0xf1, 0x58, 0x8e, 0x81, 0x4b, 0x3a, 0x33,
	0x5f, 0xaf, 0x24, 0x35, 0xb7, 0x20, 0xbf, 0x28, 0x31, 0x07, 0xe2, 0x62, 0x3d, 0xb8, 0x83, 0x03,
	0x18, 0xa3, 0xd4, 0xd3, 0x91, 0xa4, 0x33, 0xf3, 0xf5, 0x61, 0x6c, 0x5d, 0xb0, 0x52, 0x7d, 0xb8,
	0xd2, 0x24, 0x36, 0xb0, 0x80, 0x31, 0x20, 0x00, 0x00, 0xff, 0xff, 0xef, 0x0c, 0x7e, 0x94, 0x05,
	0x01, 0x00, 0x00,
}

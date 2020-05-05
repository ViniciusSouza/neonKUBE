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
// source: event/enum.proto

package event

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

// Whenever this list of events is changed do change the function shouldBufferEvent in mutableStateBuilder.go to make sure to do the correct event ordering.
type EventType int32

const (
	EventType_WorkflowExecutionStarted                        EventType = 0
	EventType_WorkflowExecutionCompleted                      EventType = 1
	EventType_WorkflowExecutionFailed                         EventType = 2
	EventType_WorkflowExecutionTimedOut                       EventType = 3
	EventType_DecisionTaskScheduled                           EventType = 4
	EventType_DecisionTaskStarted                             EventType = 5
	EventType_DecisionTaskCompleted                           EventType = 6
	EventType_DecisionTaskTimedOut                            EventType = 7
	EventType_DecisionTaskFailed                              EventType = 8
	EventType_ActivityTaskScheduled                           EventType = 9
	EventType_ActivityTaskStarted                             EventType = 10
	EventType_ActivityTaskCompleted                           EventType = 11
	EventType_ActivityTaskFailed                              EventType = 12
	EventType_ActivityTaskTimedOut                            EventType = 13
	EventType_ActivityTaskCancelRequested                     EventType = 14
	EventType_RequestCancelActivityTaskFailed                 EventType = 15
	EventType_ActivityTaskCanceled                            EventType = 16
	EventType_TimerStarted                                    EventType = 17
	EventType_TimerFired                                      EventType = 18
	EventType_CancelTimerFailed                               EventType = 19
	EventType_TimerCanceled                                   EventType = 20
	EventType_WorkflowExecutionCancelRequested                EventType = 21
	EventType_WorkflowExecutionCanceled                       EventType = 22
	EventType_RequestCancelExternalWorkflowExecutionInitiated EventType = 23
	EventType_RequestCancelExternalWorkflowExecutionFailed    EventType = 24
	EventType_ExternalWorkflowExecutionCancelRequested        EventType = 25
	EventType_MarkerRecorded                                  EventType = 26
	EventType_WorkflowExecutionSignaled                       EventType = 27
	EventType_WorkflowExecutionTerminated                     EventType = 28
	EventType_WorkflowExecutionContinuedAsNew                 EventType = 29
	EventType_StartChildWorkflowExecutionInitiated            EventType = 30
	EventType_StartChildWorkflowExecutionFailed               EventType = 31
	EventType_ChildWorkflowExecutionStarted                   EventType = 32
	EventType_ChildWorkflowExecutionCompleted                 EventType = 33
	EventType_ChildWorkflowExecutionFailed                    EventType = 34
	EventType_ChildWorkflowExecutionCanceled                  EventType = 35
	EventType_ChildWorkflowExecutionTimedOut                  EventType = 36
	EventType_ChildWorkflowExecutionTerminated                EventType = 37
	EventType_SignalExternalWorkflowExecutionInitiated        EventType = 38
	EventType_SignalExternalWorkflowExecutionFailed           EventType = 39
	EventType_ExternalWorkflowExecutionSignaled               EventType = 40
	EventType_UpsertWorkflowSearchAttributes                  EventType = 41
)

var EventType_name = map[int32]string{
	0:  "WorkflowExecutionStarted",
	1:  "WorkflowExecutionCompleted",
	2:  "WorkflowExecutionFailed",
	3:  "WorkflowExecutionTimedOut",
	4:  "DecisionTaskScheduled",
	5:  "DecisionTaskStarted",
	6:  "DecisionTaskCompleted",
	7:  "DecisionTaskTimedOut",
	8:  "DecisionTaskFailed",
	9:  "ActivityTaskScheduled",
	10: "ActivityTaskStarted",
	11: "ActivityTaskCompleted",
	12: "ActivityTaskFailed",
	13: "ActivityTaskTimedOut",
	14: "ActivityTaskCancelRequested",
	15: "RequestCancelActivityTaskFailed",
	16: "ActivityTaskCanceled",
	17: "TimerStarted",
	18: "TimerFired",
	19: "CancelTimerFailed",
	20: "TimerCanceled",
	21: "WorkflowExecutionCancelRequested",
	22: "WorkflowExecutionCanceled",
	23: "RequestCancelExternalWorkflowExecutionInitiated",
	24: "RequestCancelExternalWorkflowExecutionFailed",
	25: "ExternalWorkflowExecutionCancelRequested",
	26: "MarkerRecorded",
	27: "WorkflowExecutionSignaled",
	28: "WorkflowExecutionTerminated",
	29: "WorkflowExecutionContinuedAsNew",
	30: "StartChildWorkflowExecutionInitiated",
	31: "StartChildWorkflowExecutionFailed",
	32: "ChildWorkflowExecutionStarted",
	33: "ChildWorkflowExecutionCompleted",
	34: "ChildWorkflowExecutionFailed",
	35: "ChildWorkflowExecutionCanceled",
	36: "ChildWorkflowExecutionTimedOut",
	37: "ChildWorkflowExecutionTerminated",
	38: "SignalExternalWorkflowExecutionInitiated",
	39: "SignalExternalWorkflowExecutionFailed",
	40: "ExternalWorkflowExecutionSignaled",
	41: "UpsertWorkflowSearchAttributes",
}

var EventType_value = map[string]int32{
	"WorkflowExecutionStarted":                        0,
	"WorkflowExecutionCompleted":                      1,
	"WorkflowExecutionFailed":                         2,
	"WorkflowExecutionTimedOut":                       3,
	"DecisionTaskScheduled":                           4,
	"DecisionTaskStarted":                             5,
	"DecisionTaskCompleted":                           6,
	"DecisionTaskTimedOut":                            7,
	"DecisionTaskFailed":                              8,
	"ActivityTaskScheduled":                           9,
	"ActivityTaskStarted":                             10,
	"ActivityTaskCompleted":                           11,
	"ActivityTaskFailed":                              12,
	"ActivityTaskTimedOut":                            13,
	"ActivityTaskCancelRequested":                     14,
	"RequestCancelActivityTaskFailed":                 15,
	"ActivityTaskCanceled":                            16,
	"TimerStarted":                                    17,
	"TimerFired":                                      18,
	"CancelTimerFailed":                               19,
	"TimerCanceled":                                   20,
	"WorkflowExecutionCancelRequested":                21,
	"WorkflowExecutionCanceled":                       22,
	"RequestCancelExternalWorkflowExecutionInitiated": 23,
	"RequestCancelExternalWorkflowExecutionFailed":    24,
	"ExternalWorkflowExecutionCancelRequested":        25,
	"MarkerRecorded":                                  26,
	"WorkflowExecutionSignaled":                       27,
	"WorkflowExecutionTerminated":                     28,
	"WorkflowExecutionContinuedAsNew":                 29,
	"StartChildWorkflowExecutionInitiated":            30,
	"StartChildWorkflowExecutionFailed":               31,
	"ChildWorkflowExecutionStarted":                   32,
	"ChildWorkflowExecutionCompleted":                 33,
	"ChildWorkflowExecutionFailed":                    34,
	"ChildWorkflowExecutionCanceled":                  35,
	"ChildWorkflowExecutionTimedOut":                  36,
	"ChildWorkflowExecutionTerminated":                37,
	"SignalExternalWorkflowExecutionInitiated":        38,
	"SignalExternalWorkflowExecutionFailed":           39,
	"ExternalWorkflowExecutionSignaled":               40,
	"UpsertWorkflowSearchAttributes":                  41,
}

func (x EventType) String() string {
	return proto.EnumName(EventType_name, int32(x))
}

func (EventType) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_274c373c015eaec4, []int{0}
}

type DecisionTaskFailedCause int32

const (
	DecisionTaskFailedCause_UnhandledDecision                                   DecisionTaskFailedCause = 0
	DecisionTaskFailedCause_BadScheduleActivityAttributes                       DecisionTaskFailedCause = 1
	DecisionTaskFailedCause_BadRequestCancelActivityAttributes                  DecisionTaskFailedCause = 2
	DecisionTaskFailedCause_BadStartTimerAttributes                             DecisionTaskFailedCause = 3
	DecisionTaskFailedCause_BadCancelTimerAttributes                            DecisionTaskFailedCause = 4
	DecisionTaskFailedCause_BadRecordMarkerAttributes                           DecisionTaskFailedCause = 5
	DecisionTaskFailedCause_BadCompleteWorkflowExecutionAttributes              DecisionTaskFailedCause = 6
	DecisionTaskFailedCause_BadFailWorkflowExecutionAttributes                  DecisionTaskFailedCause = 7
	DecisionTaskFailedCause_BadCancelWorkflowExecutionAttributes                DecisionTaskFailedCause = 8
	DecisionTaskFailedCause_BadRequestCancelExternalWorkflowExecutionAttributes DecisionTaskFailedCause = 9
	DecisionTaskFailedCause_BadContinueAsNewAttributes                          DecisionTaskFailedCause = 10
	DecisionTaskFailedCause_StartTimerDuplicateId                               DecisionTaskFailedCause = 11
	DecisionTaskFailedCause_ResetStickyTasklist                                 DecisionTaskFailedCause = 12
	DecisionTaskFailedCause_WorkflowWorkerUnhandledFailure                      DecisionTaskFailedCause = 13
	DecisionTaskFailedCause_BadSignalWorkflowExecutionAttributes                DecisionTaskFailedCause = 14
	DecisionTaskFailedCause_BadStartChildExecutionAttributes                    DecisionTaskFailedCause = 15
	DecisionTaskFailedCause_ForceCloseDecision                                  DecisionTaskFailedCause = 16
	DecisionTaskFailedCause_FailoverCloseDecision                               DecisionTaskFailedCause = 17
	DecisionTaskFailedCause_BadSignalInputSize                                  DecisionTaskFailedCause = 18
	DecisionTaskFailedCause_ResetWorkflow                                       DecisionTaskFailedCause = 19
	DecisionTaskFailedCause_BadBinary                                           DecisionTaskFailedCause = 20
	DecisionTaskFailedCause_ScheduleActivityDuplicateId                         DecisionTaskFailedCause = 21
	DecisionTaskFailedCause_BadSearchAttributes                                 DecisionTaskFailedCause = 22
)

var DecisionTaskFailedCause_name = map[int32]string{
	0:  "UnhandledDecision",
	1:  "BadScheduleActivityAttributes",
	2:  "BadRequestCancelActivityAttributes",
	3:  "BadStartTimerAttributes",
	4:  "BadCancelTimerAttributes",
	5:  "BadRecordMarkerAttributes",
	6:  "BadCompleteWorkflowExecutionAttributes",
	7:  "BadFailWorkflowExecutionAttributes",
	8:  "BadCancelWorkflowExecutionAttributes",
	9:  "BadRequestCancelExternalWorkflowExecutionAttributes",
	10: "BadContinueAsNewAttributes",
	11: "StartTimerDuplicateId",
	12: "ResetStickyTasklist",
	13: "WorkflowWorkerUnhandledFailure",
	14: "BadSignalWorkflowExecutionAttributes",
	15: "BadStartChildExecutionAttributes",
	16: "ForceCloseDecision",
	17: "FailoverCloseDecision",
	18: "BadSignalInputSize",
	19: "ResetWorkflow",
	20: "BadBinary",
	21: "ScheduleActivityDuplicateId",
	22: "BadSearchAttributes",
}

var DecisionTaskFailedCause_value = map[string]int32{
	"UnhandledDecision":                                   0,
	"BadScheduleActivityAttributes":                       1,
	"BadRequestCancelActivityAttributes":                  2,
	"BadStartTimerAttributes":                             3,
	"BadCancelTimerAttributes":                            4,
	"BadRecordMarkerAttributes":                           5,
	"BadCompleteWorkflowExecutionAttributes":              6,
	"BadFailWorkflowExecutionAttributes":                  7,
	"BadCancelWorkflowExecutionAttributes":                8,
	"BadRequestCancelExternalWorkflowExecutionAttributes": 9,
	"BadContinueAsNewAttributes":                          10,
	"StartTimerDuplicateId":                               11,
	"ResetStickyTasklist":                                 12,
	"WorkflowWorkerUnhandledFailure":                      13,
	"BadSignalWorkflowExecutionAttributes":                14,
	"BadStartChildExecutionAttributes":                    15,
	"ForceCloseDecision":                                  16,
	"FailoverCloseDecision":                               17,
	"BadSignalInputSize":                                  18,
	"ResetWorkflow":                                       19,
	"BadBinary":                                           20,
	"ScheduleActivityDuplicateId":                         21,
	"BadSearchAttributes":                                 22,
}

func (x DecisionTaskFailedCause) String() string {
	return proto.EnumName(DecisionTaskFailedCause_name, int32(x))
}

func (DecisionTaskFailedCause) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_274c373c015eaec4, []int{1}
}

type TimeoutType int32

const (
	TimeoutType_StartToClose    TimeoutType = 0
	TimeoutType_ScheduleToStart TimeoutType = 1
	TimeoutType_ScheduleToClose TimeoutType = 2
	TimeoutType_Heartbeat       TimeoutType = 3
)

var TimeoutType_name = map[int32]string{
	0: "StartToClose",
	1: "ScheduleToStart",
	2: "ScheduleToClose",
	3: "Heartbeat",
}

var TimeoutType_value = map[string]int32{
	"StartToClose":    0,
	"ScheduleToStart": 1,
	"ScheduleToClose": 2,
	"Heartbeat":       3,
}

func (x TimeoutType) String() string {
	return proto.EnumName(TimeoutType_name, int32(x))
}

func (TimeoutType) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_274c373c015eaec4, []int{2}
}

type WorkflowExecutionFailedCause int32

const (
	WorkflowExecutionFailedCause_UnknownExternalWorkflowExecution WorkflowExecutionFailedCause = 0
	WorkflowExecutionFailedCause_WorkflowAlreadyRunning           WorkflowExecutionFailedCause = 1
)

var WorkflowExecutionFailedCause_name = map[int32]string{
	0: "UnknownExternalWorkflowExecution",
	1: "WorkflowAlreadyRunning",
}

var WorkflowExecutionFailedCause_value = map[string]int32{
	"UnknownExternalWorkflowExecution": 0,
	"WorkflowAlreadyRunning":           1,
}

func (x WorkflowExecutionFailedCause) String() string {
	return proto.EnumName(WorkflowExecutionFailedCause_name, int32(x))
}

func (WorkflowExecutionFailedCause) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_274c373c015eaec4, []int{3}
}

func init() {
	proto.RegisterEnum("event.EventType", EventType_name, EventType_value)
	proto.RegisterEnum("event.DecisionTaskFailedCause", DecisionTaskFailedCause_name, DecisionTaskFailedCause_value)
	proto.RegisterEnum("event.TimeoutType", TimeoutType_name, TimeoutType_value)
	proto.RegisterEnum("event.WorkflowExecutionFailedCause", WorkflowExecutionFailedCause_name, WorkflowExecutionFailedCause_value)
}

func init() { proto.RegisterFile("event/enum.proto", fileDescriptor_274c373c015eaec4) }

var fileDescriptor_274c373c015eaec4 = []byte{
	// 928 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x8c, 0x56, 0xc9, 0x52, 0x23, 0x47,
	0x13, 0x96, 0x60, 0x60, 0x86, 0x1c, 0x96, 0x22, 0x59, 0xc4, 0x2a, 0x76, 0x7e, 0x86, 0x98, 0x1f,
	0x1c, 0xc1, 0xc1, 0x67, 0xc4, 0x40, 0x98, 0x83, 0x97, 0x40, 0x10, 0x9e, 0xf0, 0xc9, 0x45, 0x77,
	0x1a, 0x2a, 0xd4, 0xaa, 0x96, 0xab, 0xab, 0x61, 0xf0, 0x53, 0xf8, 0xb1, 0x7c, 0x9c, 0x08, 0x5f,
	0x7c, 0x74, 0xc0, 0x63, 0xf8, 0xe2, 0xc8, 0xea, 0x45, 0xad, 0xd5, 0x73, 0x92, 0xba, 0xf2, 0xab,
	0xcc, 0x2f, 0x33, 0xbf, 0xcc, 0x6e, 0x10, 0xf4, 0x40, 0xda, 0x1e, 0x93, 0x8e, 0x9b, 0x47, 0x2d,
	0x13, 0xda, 0x10, 0xc7, 0xdc, 0xc9, 0xe1, 0x9f, 0x00, 0x13, 0xe7, 0xfc, 0xef, 0xfa, 0xa9, 0x45,
	0xb8, 0x06, 0x4b, 0x3f, 0x86, 0xa6, 0xf1, 0x4b, 0x10, 0x3e, 0x9e, 0x7f, 0x22, 0x2f, 0xb6, 0x2a,
	0xd4, 0x75, 0x2b, 0x8d, 0x25, 0x5f, 0x94, 0xb0, 0x0a, 0x2b, 0x3d, 0xd6, 0xb3, 0xb0, 0xd9, 0x0a,
	0x88, 0xed, 0x65, 0x5c, 0x85, 0x4a, 0x8f, 0xfd, 0x42, 0xaa, 0x80, 0x7c, 0x31, 0x82, 0xeb, 0xb0,
	0xdc, 0x63, 0xbc, 0x56, 0x4d, 0xf2, 0xbf, 0x8f, 0xad, 0x18, 0xc5, 0x65, 0x58, 0xf8, 0x40, 0x9e,
	0x8a, 0xf8, 0x54, 0x46, 0x8d, 0xba, 0x77, 0x4f, 0x7e, 0xcc, 0x37, 0x5f, 0x61, 0x05, 0xe6, 0x3a,
	0x4c, 0x29, 0x9f, 0xb1, 0xee, 0x3b, 0x6d, 0x2a, 0xe3, 0xb8, 0x04, 0xf3, 0x45, 0x53, 0x1e, 0xe8,
	0x35, 0x2e, 0x02, 0x16, 0x2d, 0x29, 0xbf, 0x37, 0xec, 0xec, 0xd4, 0xb3, 0xea, 0x41, 0xd9, 0xa7,
	0x4e, 0x02, 0x13, 0x4c, 0xa0, 0xc3, 0x94, 0x12, 0x80, 0xee, 0x3b, 0x6d, 0x02, 0x6f, 0x39, 0x4c,
	0xd1, 0x94, 0x86, 0x99, 0x64, 0x62, 0xc5, 0xf3, 0x9c, 0xd8, 0x14, 0x6e, 0xc0, 0x6a, 0x87, 0x33,
	0xa9, 0x3d, 0x0a, 0xae, 0xe8, 0xd7, 0x98, 0x22, 0x76, 0x39, 0x8d, 0x3b, 0xb0, 0x91, 0x3e, 0x26,
	0xb6, 0x3e, 0xfe, 0x67, 0xba, 0xfd, 0x27, 0x48, 0xf2, 0x85, 0x40, 0x01, 0x93, 0x1c, 0xcd, 0x64,
	0xf4, 0x67, 0x71, 0x1a, 0xc0, 0x9d, 0x5c, 0x28, 0x43, 0xbe, 0x40, 0x5c, 0x80, 0xd9, 0x04, 0x9f,
	0x9c, 0x26, 0x2e, 0xe7, 0x70, 0x16, 0xa6, 0xdc, 0x41, 0xee, 0x6b, 0x1e, 0x77, 0x61, 0xb3, 0x57,
	0x09, 0x5d, 0x84, 0x17, 0xfa, 0xb6, 0x3c, 0x77, 0xb2, 0x88, 0x27, 0x70, 0xdc, 0x91, 0xcf, 0xf9,
	0x27, 0x4b, 0x46, 0xcb, 0xa0, 0xe7, 0xce, 0xa5, 0x56, 0x56, 0x49, 0xf6, 0x59, 0xc1, 0xaf, 0xe0,
	0xfd, 0x97, 0x5d, 0x4a, 0xe9, 0x2f, 0xe1, 0x7b, 0x38, 0x18, 0x08, 0xea, 0xe6, 0xbc, 0x8c, 0x08,
	0xd3, 0xdf, 0x4a, 0xd3, 0x20, 0x73, 0x45, 0x5e, 0x68, 0x7c, 0xf2, 0xc5, 0x4a, 0xdf, 0x3c, 0xea,
	0xea, 0x4e, 0x4b, 0x0e, 0xb0, 0xca, 0x8d, 0xeb, 0x55, 0x36, 0x99, 0xa6, 0xd2, 0x8e, 0xf3, 0x1a,
	0x37, 0xae, 0xcf, 0xdc, 0x68, 0xab, 0x74, 0x4c, 0xfe, 0x69, 0xf4, 0x1d, 0x3d, 0x8a, 0x75, 0x3c,
	0x80, 0x5d, 0xd7, 0x99, 0xb3, 0x7b, 0x15, 0xf8, 0x43, 0x4a, 0x50, 0xc5, 0x3d, 0xd8, 0x1a, 0x82,
	0x4c, 0xf3, 0xde, 0xc0, 0x2d, 0x58, 0xef, 0x8f, 0xc8, 0x04, 0xb0, 0xc9, 0xc4, 0xfa, 0x43, 0xda,
	0x4a, 0xde, 0xc2, 0x4d, 0x58, 0x1b, 0x1a, 0x69, 0x1b, 0xb7, 0xa1, 0x3a, 0xc0, 0x4d, 0xd6, 0xec,
	0x9d, 0xc1, 0x98, 0x7c, 0x02, 0x76, 0x59, 0x55, 0x03, 0x30, 0xed, 0x6a, 0xee, 0x71, 0x3f, 0x93,
	0xe2, 0x7f, 0x81, 0x5e, 0xf6, 0xf1, 0x1d, 0xec, 0xfd, 0x07, 0x3a, 0x4d, 0xe3, 0x7f, 0x5c, 0xd7,
	0x81, 0xa0, 0xbc, 0xdd, 0x07, 0x9c, 0xc9, 0x4d, 0x2b, 0x22, 0x63, 0x33, 0x50, 0x9d, 0xa4, 0xf1,
	0xee, 0x4f, 0xad, 0x35, 0xea, 0x36, 0xb6, 0x14, 0x89, 0x77, 0x87, 0xff, 0x8c, 0x41, 0xa5, 0x77,
	0xcb, 0x9c, 0xc9, 0x38, 0x22, 0x9e, 0xb2, 0x1b, 0x7d, 0x2f, 0xb5, 0x1f, 0x90, 0x9f, 0x61, 0x44,
	0x89, 0xdb, 0x55, 0x93, 0x7e, 0xb6, 0x76, 0xb2, 0x19, 0x2e, 0x78, 0x2d, 0xe3, 0x3e, 0x6c, 0xd7,
	0xa4, 0xdf, 0x77, 0x07, 0x14, 0x70, 0x23, 0xbc, 0x87, 0xd9, 0x15, 0xb7, 0xd9, 0x0d, 0x6e, 0xc1,
	0x38, 0xca, 0x2b, 0xbe, 0x26, 0xfd, 0xc2, 0x9c, 0x17, 0xac, 0xaf, 0x58, 0xea, 0x2e, 0x04, 0x6b,
	0x3f, 0x99, 0x83, 0x82, 0x79, 0x0c, 0x0f, 0x61, 0x9f, 0x2f, 0xa7, 0xea, 0xe8, 0xa9, 0x52, 0x01,
	0x3b, 0x9e, 0xb2, 0xe5, 0xcc, 0x87, 0xe1, 0x5e, 0xb3, 0xf0, 0x73, 0x42, 0xc3, 0x90, 0x6f, 0xf0,
	0x6b, 0x38, 0xe9, 0xce, 0x7f, 0x60, 0xc3, 0x0a, 0x17, 0x27, 0xf8, 0xc5, 0xe5, 0x68, 0x27, 0x23,
	0xe7, 0x26, 0xae, 0x60, 0x77, 0x7b, 0xbc, 0x5d, 0xad, 0x0f, 0x71, 0x2b, 0x50, 0x9e, 0xb4, 0x74,
	0xc9, 0x7b, 0xbc, 0x02, 0x73, 0x57, 0x14, 0x91, 0xad, 0x5b, 0xe5, 0x35, 0xdc, 0x4a, 0x0d, 0x54,
	0x64, 0xc5, 0x24, 0xcb, 0x20, 0x0b, 0xca, 0xbf, 0x64, 0xf2, 0xa6, 0x72, 0xca, 0xb1, 0x21, 0x31,
	0x95, 0xa6, 0x96, 0x68, 0x67, 0x18, 0xc3, 0x69, 0x96, 0x7e, 0xd6, 0x32, 0x37, 0x02, 0xfd, 0x50,
	0x33, 0xfc, 0x52, 0xb9, 0x08, 0x8d, 0x47, 0x67, 0x41, 0x18, 0x51, 0xae, 0x1d, 0xc1, 0xfc, 0x39,
	0x68, 0xf8, 0x40, 0xa6, 0xd3, 0x34, 0xcb, 0x57, 0x72, 0x0a, 0x97, 0xba, 0x15, 0xdb, 0xba, 0xfa,
	0x8d, 0x04, 0xf2, 0x52, 0x77, 0x79, 0x65, 0xb4, 0xc4, 0x1c, 0x4e, 0xc1, 0x44, 0x4d, 0xfa, 0x35,
	0xa5, 0xa5, 0x79, 0x12, 0xf3, 0xbc, 0xd6, 0xba, 0xd5, 0x58, 0x2c, 0xcd, 0x02, 0x97, 0x86, 0x5d,
	0x77, 0xab, 0x7f, 0xf1, 0xf0, 0x23, 0xbc, 0xe5, 0x4a, 0x86, 0x71, 0xf2, 0x51, 0x21, 0x60, 0x32,
	0xa9, 0x6e, 0xe8, 0xc8, 0x89, 0x12, 0xce, 0xc1, 0x4c, 0xe6, 0xfa, 0x3a, 0x74, 0x36, 0x51, 0xee,
	0x3c, 0x4c, 0x90, 0x23, 0xcc, 0xe9, 0x1b, 0x92, 0xc6, 0xde, 0x92, 0xb4, 0x62, 0xf4, 0xf0, 0x67,
	0x58, 0x1b, 0x30, 0xbf, 0xc9, 0x6c, 0xed, 0xc2, 0xe6, 0x8d, 0x6e, 0xe8, 0xf0, 0x51, 0x0f, 0x14,
	0x86, 0x28, 0xe1, 0x0a, 0x2c, 0x66, 0xc7, 0xa7, 0x81, 0x21, 0xe9, 0x3f, 0x5d, 0xc5, 0x5a, 0x2b,
	0x7d, 0x27, 0xca, 0xb5, 0x8f, 0x7f, 0x3c, 0x57, 0xcb, 0x9f, 0x9f, 0xab, 0xe5, 0xbf, 0x9f, 0xab,
	0xe5, 0xdf, 0x5f, 0xaa, 0xa5, 0xcf, 0x2f, 0xd5, 0xd2, 0x5f, 0x2f, 0xd5, 0x12, 0x54, 0x54, 0x78,
	0x64, 0xa9, 0xd9, 0x0a, 0x8d, 0x0c, 0x92, 0x6f, 0xa8, 0x23, 0xf7, 0x09, 0xf5, 0x43, 0xf9, 0xa7,
	0x9d, 0xbb, 0x82, 0x49, 0x85, 0xc7, 0xd9, 0xff, 0xff, 0x3b, 0xd8, 0xb1, 0x83, 0xdd, 0x8e, 0xbb,
	0x87, 0x93, 0x7f, 0x03, 0x00, 0x00, 0xff, 0xff, 0xcc, 0xfd, 0xee, 0x61, 0x8b, 0x09, 0x00, 0x00,
}

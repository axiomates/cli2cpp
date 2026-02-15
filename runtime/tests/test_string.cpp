/**
 * CIL2CPP Runtime Tests - String
 */

#include <gtest/gtest.h>
#include <cil2cpp/cil2cpp.h>
#include <cstring>
#include <cstdlib>

using namespace cil2cpp;

class StringTest : public ::testing::Test {
protected:
    void SetUp() override {
        runtime_init();
    }

    void TearDown() override {
        runtime_shutdown();
    }
};

// ===== string_create_utf8 =====

TEST_F(StringTest, CreateUtf8_SimpleAscii) {
    String* str = string_create_utf8("Hello");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 5);
}

TEST_F(StringTest, CreateUtf8_EmptyString) {
    String* str = string_create_utf8("");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 0);
}

TEST_F(StringTest, CreateUtf8_NullReturnsNull) {
    String* str = string_create_utf8(nullptr);
    EXPECT_EQ(str, nullptr);
}

TEST_F(StringTest, CreateUtf8_AsciiContent) {
    String* str = string_create_utf8("ABC");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->chars[0], u'A');
    EXPECT_EQ(str->chars[1], u'B');
    EXPECT_EQ(str->chars[2], u'C');
}

TEST_F(StringTest, CreateUtf8_MultiByte) {
    // UTF-8 for "é" is 0xC3 0xA9 (2 bytes)
    String* str = string_create_utf8("\xC3\xA9");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 1);
    EXPECT_EQ(str->chars[0], u'\u00E9');
}

// ===== string_create_utf16 =====

TEST_F(StringTest, CreateUtf16_Basic) {
    Char data[] = { u'H', u'i' };
    String* str = string_create_utf16(data, 2);
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 2);
    EXPECT_EQ(str->chars[0], u'H');
    EXPECT_EQ(str->chars[1], u'i');
}

TEST_F(StringTest, CreateUtf16_NullReturnsNull) {
    String* str = string_create_utf16(nullptr, 5);
    EXPECT_EQ(str, nullptr);
}

TEST_F(StringTest, CreateUtf16_NegativeLength_ReturnsNull) {
    Char data[] = { u'A' };
    String* str = string_create_utf16(data, -1);
    EXPECT_EQ(str, nullptr);
}

// ===== string_literal (interning) =====

TEST_F(StringTest, Literal_ReturnsSamePointer) {
    String* a = string_literal("test");
    String* b = string_literal("test");
    EXPECT_EQ(a, b);  // Same pointer = interned
}

TEST_F(StringTest, Literal_DifferentStrings_DifferentPointers) {
    String* a = string_literal("hello");
    String* b = string_literal("world");
    EXPECT_NE(a, b);
}

TEST_F(StringTest, Literal_NullReturnsNull) {
    String* str = string_literal(nullptr);
    EXPECT_EQ(str, nullptr);
}

// ===== string_concat =====

TEST_F(StringTest, Concat_TwoStrings) {
    String* a = string_create_utf8("Hello, ");
    String* b = string_create_utf8("World!");
    String* result = string_concat(a, b);

    ASSERT_NE(result, nullptr);
    EXPECT_EQ(result->length, 13);

    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello, World!");
    std::free(utf8);
}

TEST_F(StringTest, Concat_NullA_ReturnsB) {
    String* b = string_create_utf8("test");
    EXPECT_EQ(string_concat(nullptr, b), b);
}

TEST_F(StringTest, Concat_NullB_ReturnsA) {
    String* a = string_create_utf8("test");
    EXPECT_EQ(string_concat(a, nullptr), a);
}

// ===== string_equals =====

TEST_F(StringTest, Equals_SameContent_True) {
    String* a = string_create_utf8("hello");
    String* b = string_create_utf8("hello");
    EXPECT_TRUE(string_equals(a, b));
}

TEST_F(StringTest, Equals_DifferentContent_False) {
    String* a = string_create_utf8("hello");
    String* b = string_create_utf8("world");
    EXPECT_FALSE(string_equals(a, b));
}

TEST_F(StringTest, Equals_DifferentLengths_False) {
    String* a = string_create_utf8("hi");
    String* b = string_create_utf8("hello");
    EXPECT_FALSE(string_equals(a, b));
}

TEST_F(StringTest, Equals_SamePointer_True) {
    String* a = string_create_utf8("test");
    EXPECT_TRUE(string_equals(a, a));
}

TEST_F(StringTest, Equals_NullNull_True) {
    // null == null returns true (same pointer check: a == b)
    EXPECT_TRUE(string_equals(nullptr, nullptr));
}

TEST_F(StringTest, Equals_OneNull_False) {
    String* a = string_create_utf8("test");
    EXPECT_FALSE(string_equals(a, nullptr));
    EXPECT_FALSE(string_equals(nullptr, a));
}

// ===== string_get_hash_code =====

TEST_F(StringTest, HashCode_SameString_SameHash) {
    String* a = string_create_utf8("hello");
    String* b = string_create_utf8("hello");
    EXPECT_EQ(string_get_hash_code(a), string_get_hash_code(b));
}

TEST_F(StringTest, HashCode_DifferentStrings_LikelyDifferentHash) {
    String* a = string_create_utf8("hello");
    String* b = string_create_utf8("world");
    // Hash collision is possible but unlikely for these strings
    EXPECT_NE(string_get_hash_code(a), string_get_hash_code(b));
}

TEST_F(StringTest, HashCode_Null_ReturnsZero) {
    EXPECT_EQ(string_get_hash_code(nullptr), 0);
}

// ===== string_is_null_or_empty =====

TEST_F(StringTest, IsNullOrEmpty_Null_True) {
    EXPECT_TRUE(string_is_null_or_empty(nullptr));
}

TEST_F(StringTest, IsNullOrEmpty_Empty_True) {
    String* str = string_create_utf8("");
    EXPECT_TRUE(string_is_null_or_empty(str));
}

TEST_F(StringTest, IsNullOrEmpty_NonEmpty_False) {
    String* str = string_create_utf8("a");
    EXPECT_FALSE(string_is_null_or_empty(str));
}

// ===== string_substring =====

TEST_F(StringTest, Substring_Middle) {
    String* str = string_create_utf8("Hello, World!");
    String* sub = string_substring(str, 7, 5);
    ASSERT_NE(sub, nullptr);

    char* utf8 = string_to_utf8(sub);
    EXPECT_STREQ(utf8, "World");
    std::free(utf8);
}

TEST_F(StringTest, Substring_NullString_ReturnsNull) {
    EXPECT_EQ(string_substring(nullptr, 0, 5), nullptr);
}

TEST_F(StringTest, Substring_OutOfBounds_ReturnsNull) {
    String* str = string_create_utf8("Hi");
    EXPECT_EQ(string_substring(str, 0, 10), nullptr);
}

TEST_F(StringTest, Substring_NegativeStart_ReturnsNull) {
    String* str = string_create_utf8("Hi");
    EXPECT_EQ(string_substring(str, -1, 1), nullptr);
}

// ===== string_to_utf8 =====

TEST_F(StringTest, ToUtf8_RoundTrip) {
    const char* original = "Hello, CIL2CPP!";
    String* str = string_create_utf8(original);
    char* result = string_to_utf8(str);
    ASSERT_NE(result, nullptr);
    EXPECT_STREQ(result, original);
    std::free(result);
}

TEST_F(StringTest, ToUtf8_NullReturnsNull) {
    EXPECT_EQ(string_to_utf8(nullptr), nullptr);
}

// ===== string_length =====

TEST_F(StringTest, Length_NonNull) {
    String* str = string_create_utf8("test");
    EXPECT_EQ(string_length(str), 4);
}

TEST_F(StringTest, Length_Null_ReturnsZero) {
    EXPECT_EQ(string_length(nullptr), 0);
}

// ===== String::get_length() member function =====

TEST_F(StringTest, MemberGetLength_ReturnsLength) {
    String* str = string_create_utf8("Hello");
    EXPECT_EQ(str->get_length(), 5);
}

TEST_F(StringTest, MemberGetLength_Empty) {
    String* str = string_create_utf8("");
    EXPECT_EQ(str->get_length(), 0);
}

// ===== String::get_char() member function =====

TEST_F(StringTest, MemberGetChar_ReturnsCorrectChar) {
    String* str = string_create_utf8("ABC");
    EXPECT_EQ(str->get_char(0), u'A');
    EXPECT_EQ(str->get_char(1), u'B');
    EXPECT_EQ(str->get_char(2), u'C');
}

// ===== UTF-8 3-byte characters (CJK) =====

TEST_F(StringTest, CreateUtf8_ThreeByte_CJK) {
    // UTF-8 for U+4F60 is E4 BD A0 (3 bytes)
    String* str = string_create_utf8("\xE4\xBD\xA0");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 1);
    EXPECT_EQ(str->chars[0], 0x4F60);
}

TEST_F(StringTest, CreateUtf8_ThreeByte_Mixed) {
    // "A" + 3-byte CJK + "B"
    String* str = string_create_utf8("A\xE4\xBD\xA0\x42");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 3);
    EXPECT_EQ(str->chars[0], u'A');
    EXPECT_EQ(str->chars[1], 0x4F60);
    EXPECT_EQ(str->chars[2], u'B');
}

// ===== UTF-8 4-byte characters (emoji/supplementary plane) =====

TEST_F(StringTest, CreateUtf8_FourByte_Emoji) {
    // UTF-8 for U+1F600 is F0 9F 98 80 (4 bytes)
    // In UTF-16 this becomes a surrogate pair: D83D DE00
    String* str = string_create_utf8("\xF0\x9F\x98\x80");
    ASSERT_NE(str, nullptr);
    EXPECT_EQ(str->length, 2);  // Surrogate pair = 2 UTF-16 code units
    EXPECT_EQ(str->chars[0], 0xD83D);  // High surrogate
    EXPECT_EQ(str->chars[1], 0xDE00);  // Low surrogate
}

// ===== string_concat edge cases =====

TEST_F(StringTest, Concat_BothEmpty) {
    String* a = string_create_utf8("");
    String* b = string_create_utf8("");
    String* result = string_concat(a, b);
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(result->length, 0);
}

TEST_F(StringTest, Concat_EmptyAndNonEmpty) {
    String* a = string_create_utf8("");
    String* b = string_create_utf8("test");
    String* result = string_concat(a, b);
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(result->length, 4);
}

TEST_F(StringTest, Concat_BothNull_ReturnsNull) {
    EXPECT_EQ(string_concat(nullptr, nullptr), nullptr);
}

// ===== string_substring edge cases =====

TEST_F(StringTest, Substring_FromStart) {
    String* str = string_create_utf8("Hello, World!");
    String* sub = string_substring(str, 0, 5);
    ASSERT_NE(sub, nullptr);

    char* utf8 = string_to_utf8(sub);
    EXPECT_STREQ(utf8, "Hello");
    std::free(utf8);
}

TEST_F(StringTest, Substring_EntireString) {
    String* str = string_create_utf8("Hi");
    String* sub = string_substring(str, 0, 2);
    ASSERT_NE(sub, nullptr);
    EXPECT_EQ(sub->length, 2);
}

TEST_F(StringTest, Substring_ZeroLength) {
    String* str = string_create_utf8("Hi");
    String* sub = string_substring(str, 0, 0);
    ASSERT_NE(sub, nullptr);
    EXPECT_EQ(sub->length, 0);
}

TEST_F(StringTest, Substring_NegativeLength_ReturnsNull) {
    String* str = string_create_utf8("Hi");
    EXPECT_EQ(string_substring(str, 0, -1), nullptr);
}

// ===== string_get_hash_code edge cases =====

TEST_F(StringTest, HashCode_EmptyString_NonZero) {
    String* str = string_create_utf8("");
    // Empty string hash should be the FNV1a offset basis, which is non-zero
    EXPECT_NE(string_get_hash_code(str), 0);
}

// ===== string_to_utf8 with multibyte =====

TEST_F(StringTest, ToUtf8_RoundTrip_MultiByte) {
    const char* original = "caf\xC3\xA9";  // 2-byte UTF-8
    String* str = string_create_utf8(original);
    char* result = string_to_utf8(str);
    ASSERT_NE(result, nullptr);
    EXPECT_STREQ(result, original);
    std::free(result);
}

TEST_F(StringTest, ToUtf8_RoundTrip_ThreeByte) {
    const char* original = "\xE4\xBD\xA0\xE5\xA5\xBD";  // 3-byte UTF-8
    String* str = string_create_utf8(original);
    char* result = string_to_utf8(str);
    ASSERT_NE(result, nullptr);
    EXPECT_STREQ(result, original);
    std::free(result);
}

// ===== string_literal caching =====

TEST_F(StringTest, Literal_MultipleCalls_StayInterned) {
    String* a = string_literal("cached_str");
    String* b = string_literal("cached_str");
    String* c = string_literal("cached_str");
    EXPECT_EQ(a, b);
    EXPECT_EQ(b, c);
}

// ===== string_equals edge cases =====

TEST_F(StringTest, Equals_EmptyStrings_True) {
    String* a = string_create_utf8("");
    String* b = string_create_utf8("");
    EXPECT_TRUE(string_equals(a, b));
}

// ===== string_index_of =====

TEST_F(StringTest, IndexOf_Found) {
    String* str = string_create_utf8("Hello, World!");
    EXPECT_EQ(string_index_of(str, u'W'), 7);
}

TEST_F(StringTest, IndexOf_NotFound) {
    String* str = string_create_utf8("Hello");
    EXPECT_EQ(string_index_of(str, u'Z'), -1);
}

TEST_F(StringTest, IndexOf_FirstOccurrence) {
    String* str = string_create_utf8("abcabc");
    EXPECT_EQ(string_index_of(str, u'b'), 1);
}

TEST_F(StringTest, IndexOf_Null) {
    EXPECT_EQ(string_index_of(nullptr, u'a'), -1);
}

// ===== string_last_index_of =====

TEST_F(StringTest, LastIndexOf_Found) {
    String* str = string_create_utf8("abcabc");
    EXPECT_EQ(string_last_index_of(str, u'b'), 4);
}

TEST_F(StringTest, LastIndexOf_NotFound) {
    String* str = string_create_utf8("Hello");
    EXPECT_EQ(string_last_index_of(str, u'Z'), -1);
}

// ===== string_contains =====

TEST_F(StringTest, Contains_CharFound) {
    String* str = string_create_utf8("Hello");
    EXPECT_TRUE(string_contains(str, u'e'));
}

TEST_F(StringTest, Contains_CharNotFound) {
    String* str = string_create_utf8("Hello");
    EXPECT_FALSE(string_contains(str, u'z'));
}

// ===== string_contains_string =====

TEST_F(StringTest, ContainsString_Found) {
    String* str = string_create_utf8("Hello, World!");
    String* sub = string_create_utf8("World");
    EXPECT_TRUE(string_contains_string(str, sub));
}

TEST_F(StringTest, ContainsString_NotFound) {
    String* str = string_create_utf8("Hello");
    String* sub = string_create_utf8("xyz");
    EXPECT_FALSE(string_contains_string(str, sub));
}

TEST_F(StringTest, ContainsString_Empty) {
    String* str = string_create_utf8("Hello");
    String* sub = string_create_utf8("");
    EXPECT_TRUE(string_contains_string(str, sub));
}

// ===== string_starts_with =====

TEST_F(StringTest, StartsWith_True) {
    String* str = string_create_utf8("Hello, World!");
    String* prefix = string_create_utf8("Hello");
    EXPECT_TRUE(string_starts_with(str, prefix));
}

TEST_F(StringTest, StartsWith_False) {
    String* str = string_create_utf8("Hello, World!");
    String* prefix = string_create_utf8("World");
    EXPECT_FALSE(string_starts_with(str, prefix));
}

TEST_F(StringTest, StartsWith_LongerPrefix) {
    String* str = string_create_utf8("Hi");
    String* prefix = string_create_utf8("Hello");
    EXPECT_FALSE(string_starts_with(str, prefix));
}

// ===== string_ends_with =====

TEST_F(StringTest, EndsWith_True) {
    String* str = string_create_utf8("Hello, World!");
    String* suffix = string_create_utf8("World!");
    EXPECT_TRUE(string_ends_with(str, suffix));
}

TEST_F(StringTest, EndsWith_False) {
    String* str = string_create_utf8("Hello, World!");
    String* suffix = string_create_utf8("Hello");
    EXPECT_FALSE(string_ends_with(str, suffix));
}

// ===== string_to_upper / string_to_lower =====

TEST_F(StringTest, ToUpper_Basic) {
    String* str = string_create_utf8("hello");
    String* result = string_to_upper(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "HELLO");
    std::free(utf8);
}

TEST_F(StringTest, ToLower_Basic) {
    String* str = string_create_utf8("HELLO");
    String* result = string_to_lower(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "hello");
    std::free(utf8);
}

TEST_F(StringTest, ToUpper_MixedCase) {
    String* str = string_create_utf8("HeLLo WoRLD");
    String* result = string_to_upper(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "HELLO WORLD");
    std::free(utf8);
}

TEST_F(StringTest, ToLower_AlreadyLower) {
    String* str = string_create_utf8("abc");
    String* result = string_to_lower(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "abc");
    std::free(utf8);
}

// ===== string_trim / string_trim_start / string_trim_end =====

TEST_F(StringTest, Trim_Both) {
    String* str = string_create_utf8("  hello  ");
    String* result = string_trim(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "hello");
    std::free(utf8);
}

TEST_F(StringTest, TrimStart_LeadingSpaces) {
    String* str = string_create_utf8("  hello  ");
    String* result = string_trim_start(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "hello  ");
    std::free(utf8);
}

TEST_F(StringTest, TrimEnd_TrailingSpaces) {
    String* str = string_create_utf8("  hello  ");
    String* result = string_trim_end(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "  hello");
    std::free(utf8);
}

TEST_F(StringTest, Trim_NoWhitespace) {
    String* str = string_create_utf8("hello");
    String* result = string_trim(str);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "hello");
    std::free(utf8);
}

TEST_F(StringTest, Trim_AllWhitespace) {
    String* str = string_create_utf8("   ");
    String* result = string_trim(str);
    EXPECT_EQ(result->length, 0);
}

// ===== string_replace =====

TEST_F(StringTest, Replace_Char) {
    String* str = string_create_utf8("hello");
    String* result = string_replace(str, u'l', u'r');
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "herro");
    std::free(utf8);
}

TEST_F(StringTest, Replace_CharNotFound) {
    String* str = string_create_utf8("hello");
    String* result = string_replace(str, u'z', u'r');
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "hello");
    std::free(utf8);
}

// ===== string_replace_string =====

TEST_F(StringTest, ReplaceString_Basic) {
    String* str = string_create_utf8("Hello, World!");
    String* oldVal = string_create_utf8("World");
    String* newVal = string_create_utf8("C++");
    String* result = string_replace_string(str, oldVal, newVal);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello, C++!");
    std::free(utf8);
}

TEST_F(StringTest, ReplaceString_Multiple) {
    String* str = string_create_utf8("aabaa");
    String* oldVal = string_create_utf8("aa");
    String* newVal = string_create_utf8("x");
    String* result = string_replace_string(str, oldVal, newVal);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "xbx");
    std::free(utf8);
}

// ===== string_remove =====

TEST_F(StringTest, Remove_FromMiddle) {
    String* str = string_create_utf8("Hello, World!");
    String* result = string_remove(str, 5, 7);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello!");
    std::free(utf8);
}

TEST_F(StringTest, Remove_ToEnd) {
    String* str = string_create_utf8("Hello, World!");
    String* result = string_remove(str, 5);  // 1-param overload: removes to end
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello");
    std::free(utf8);
}

// ===== string_insert =====

TEST_F(StringTest, Insert_Middle) {
    String* str = string_create_utf8("HelloWorld");
    String* ins = string_create_utf8(", ");
    String* result = string_insert(str, 5, ins);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello, World");
    std::free(utf8);
}

TEST_F(StringTest, Insert_AtStart) {
    String* str = string_create_utf8("World");
    String* ins = string_create_utf8("Hello ");
    String* result = string_insert(str, 0, ins);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello World");
    std::free(utf8);
}

// ===== string_pad_left / string_pad_right =====

TEST_F(StringTest, PadLeft_Basic) {
    String* str = string_create_utf8("42");
    String* result = string_pad_left(str, 5);
    EXPECT_EQ(result->length, 5);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "   42");
    std::free(utf8);
}

TEST_F(StringTest, PadRight_Basic) {
    String* str = string_create_utf8("42");
    String* result = string_pad_right(str, 5);
    EXPECT_EQ(result->length, 5);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "42   ");
    std::free(utf8);
}

TEST_F(StringTest, PadLeft_AlreadyLong) {
    String* str = string_create_utf8("Hello");
    String* result = string_pad_left(str, 3);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello");
    std::free(utf8);
}

// ===== string_compare_ordinal =====

TEST_F(StringTest, CompareOrdinal_Equal) {
    String* a = string_create_utf8("hello");
    String* b = string_create_utf8("hello");
    EXPECT_EQ(string_compare_ordinal(a, b), 0);
}

TEST_F(StringTest, CompareOrdinal_LessThan) {
    String* a = string_create_utf8("abc");
    String* b = string_create_utf8("abd");
    EXPECT_LT(string_compare_ordinal(a, b), 0);
}

TEST_F(StringTest, CompareOrdinal_GreaterThan) {
    String* a = string_create_utf8("abd");
    String* b = string_create_utf8("abc");
    EXPECT_GT(string_compare_ordinal(a, b), 0);
}

// ===== string_format =====

TEST_F(StringTest, Format_SingleArg) {
    String* fmt = string_create_utf8("Hello, {0}!");
    // Create a string arg as Object*
    String* arg = string_create_utf8("World");
    Array* args = static_cast<Array*>(gc::alloc(sizeof(Array) + sizeof(Object*), nullptr));
    args->length = 1;
    static_cast<Object**>(array_data(args))[0] = reinterpret_cast<Object*>(arg);

    String* result = string_format(fmt, args);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "Hello, World!");
    std::free(utf8);
}

TEST_F(StringTest, Format_MultipleArgs) {
    String* fmt = string_create_utf8("{0} + {1} = {0}{1}");
    String* a = string_create_utf8("A");
    String* b = string_create_utf8("B");
    Array* args = static_cast<Array*>(gc::alloc(sizeof(Array) + 2 * sizeof(Object*), nullptr));
    args->length = 2;
    static_cast<Object**>(array_data(args))[0] = reinterpret_cast<Object*>(a);
    static_cast<Object**>(array_data(args))[1] = reinterpret_cast<Object*>(b);

    String* result = string_format(fmt, args);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "A + B = AB");
    std::free(utf8);
}

TEST_F(StringTest, Format_EscapedBraces) {
    String* fmt = string_create_utf8("{{0}} is {0}");
    String* arg = string_create_utf8("zero");
    Array* args = static_cast<Array*>(gc::alloc(sizeof(Array) + sizeof(Object*), nullptr));
    args->length = 1;
    static_cast<Object**>(array_data(args))[0] = reinterpret_cast<Object*>(arg);

    String* result = string_format(fmt, args);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "{0} is zero");
    std::free(utf8);
}

// ===== string_join =====

TEST_F(StringTest, Join_Basic) {
    String* sep = string_create_utf8(", ");
    // Create array of 3 strings
    Array* arr = static_cast<Array*>(gc::alloc(sizeof(Array) + 3 * sizeof(String*), nullptr));
    arr->length = 3;
    auto** items = reinterpret_cast<String**>(array_data(arr));
    items[0] = string_create_utf8("a");
    items[1] = string_create_utf8("b");
    items[2] = string_create_utf8("c");

    String* result = string_join(sep, arr);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "a, b, c");
    std::free(utf8);
}

// ===== string_split =====

TEST_F(StringTest, Split_Basic) {
    String* str = string_create_utf8("a,b,c");
    Array* result = string_split(str, u',');
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(result->length, 3);

    auto** items = reinterpret_cast<String**>(array_data(result));
    char* s0 = string_to_utf8(items[0]);
    char* s1 = string_to_utf8(items[1]);
    char* s2 = string_to_utf8(items[2]);
    EXPECT_STREQ(s0, "a");
    EXPECT_STREQ(s1, "b");
    EXPECT_STREQ(s2, "c");
    std::free(s0);
    std::free(s1);
    std::free(s2);
}

TEST_F(StringTest, Split_NoSeparator) {
    String* str = string_create_utf8("hello");
    Array* result = string_split(str, u',');
    ASSERT_NE(result, nullptr);
    EXPECT_EQ(result->length, 1);
    auto** items = reinterpret_cast<String**>(array_data(result));
    char* s0 = string_to_utf8(items[0]);
    EXPECT_STREQ(s0, "hello");
    std::free(s0);
}

// ===== string_from_bool / string_from_char =====

TEST_F(StringTest, FromBool_True) {
    String* result = string_from_bool(true);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "True");
    std::free(utf8);
}

TEST_F(StringTest, FromBool_False) {
    String* result = string_from_bool(false);
    char* utf8 = string_to_utf8(result);
    EXPECT_STREQ(utf8, "False");
    std::free(utf8);
}

TEST_F(StringTest, FromChar_Basic) {
    String* result = string_from_char(u'X');
    EXPECT_EQ(result->length, 1);
    EXPECT_EQ(result->chars[0], u'X');
}

// ===== string_is_null_or_whitespace =====

TEST_F(StringTest, IsNullOrWhiteSpace_Null) {
    EXPECT_TRUE(string_is_null_or_whitespace(nullptr));
}

TEST_F(StringTest, IsNullOrWhiteSpace_Whitespace) {
    String* str = string_create_utf8("  \t\n ");
    EXPECT_TRUE(string_is_null_or_whitespace(str));
}

TEST_F(StringTest, IsNullOrWhiteSpace_NonWhitespace) {
    String* str = string_create_utf8(" hello ");
    EXPECT_FALSE(string_is_null_or_whitespace(str));
}

// ===== string_get_chars =====

TEST_F(StringTest, GetChars_Basic) {
    String* str = string_create_utf8("ABC");
    EXPECT_EQ(string_get_chars(str, 0), u'A');
    EXPECT_EQ(string_get_chars(str, 1), u'B');
    EXPECT_EQ(string_get_chars(str, 2), u'C');
}

// ===== Math helpers =====

TEST_F(StringTest, MathSign_Positive) {
    EXPECT_EQ(math_sign_i32(42), 1);
    EXPECT_EQ(math_sign_i64(100LL), 1);
    EXPECT_EQ(math_sign_f64(3.14), 1);
}

TEST_F(StringTest, MathSign_Negative) {
    EXPECT_EQ(math_sign_i32(-5), -1);
    EXPECT_EQ(math_sign_i64(-100LL), -1);
    EXPECT_EQ(math_sign_f64(-2.7), -1);
}

TEST_F(StringTest, MathSign_Zero) {
    EXPECT_EQ(math_sign_i32(0), 0);
    EXPECT_EQ(math_sign_i64(0LL), 0);
    EXPECT_EQ(math_sign_f64(0.0), 0);
}

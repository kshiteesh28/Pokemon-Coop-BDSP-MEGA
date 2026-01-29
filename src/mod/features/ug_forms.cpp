#include "exlaunch.hpp"

#include "externals/Dpr/UnderGround/UgPokeLottery.h"
#include "externals/Pml/Local/Random.h"
#include "externals/System/_Object.h"
#include "externals/System/Primitives.h"
#include "externals/System/Text/StringBuilder.h"
#include "externals/XLSXContent/UgEncount.h"

#include "logger/logger.h"

HOOK_DEFINE_REPLACE(Dpr_UnderGround_UgPokeLottery_LotteryPoke) {
    static void Callback(Dpr::UnderGround::UgPokeLottery::Object* __this, XLSXContent::UgEncount::Sheettable::Array* origList, System::Collections::Generic::List$$UgPokeLottery_PokeSlot::Object* slots, uint8_t rareTryCount) {
        system_load_typeinfo(0xa0b3);
        
        for (int i=0; i<slots->fields._size; i++)
        {
            uint32_t origListIdx = Pml::Local::Random::GetValue() % origList->max_length;

            int32_t inMonsNo = origList->m_Items[origListIdx]->fields.monsno;
            int32_t monsNo = inMonsNo & 0x0000FFFF;
            int32_t formNo = (inMonsNo & 0xFFFF0000) >> 16;

            Pml::PokePara::PokemonParam::Object* poke_param = __this->CreatePokemonParam_by_Tokusei(monsNo, rareTryCount);

            if (formNo != 0)
            {
                ((Pml::PokePara::CoreParam::Object*)poke_param)->ChangeFormNo((uint16_t)formNo, nullptr);
            }
            slots->fields._items->m_Items[i]->fields.param = poke_param;
        }
    }
};

HOOK_DEFINE_TRAMPOLINE(GetUgPokeData) {
    static void* Callback(int32_t monsNo) {
        return Orig(monsNo & 0x0000FFFF);
    }
};

System::String::Object* FormatUndergroundStatue(uint16_t monsno, uint16_t formno, uint8_t sex, bool shiny) {
    system_load_typeinfo(0xa6e0);

    int32_t monsno_int = monsno;
    int32_t formno_int = formno;
    int32_t sex_int = sex;
    int32_t shiny_int = shiny;

    auto args = System::_Object::newArray(4);
    args->m_Items[0] = (System::_Object::Object*)System::String::ConvertToStringObject(System::Int32_klass::getClass(), &monsno_int);
    args->m_Items[1] = (System::_Object::Object*)System::String::ConvertToStringObject(System::Int32_klass::getClass(), &formno_int);
    args->m_Items[2] = (System::_Object::Object*)System::String::ConvertToStringObject(System::Int32_klass::getClass(), &sex_int);
    args->m_Items[3] = (System::_Object::Object*)System::String::ConvertToStringObject(System::Int32_klass::getClass(), &shiny_int);
    auto str = System::String::Format(System::String::Create("pm{0:D4}_{1:D2}_{2:D1}{3:D1}"), args);

    return str;
}

HOOK_DEFINE_INLINE(UgBoxFormatNames_Load) {
    static void Callback(exl::hook::nx64::InlineCtx* ctx) {
        int32_t monsno = (uint16_t)ctx->W[20];
        int32_t formno = (uint16_t)ctx->W[21];
        int32_t sex = (uint8_t)ctx->W[22];
        int32_t shiny = (bool)ctx->W[23];

        system_load_typeinfo(0xa6e0);

        Logger::log("[UgBoxFormatNames] %d, %d, %d, %d\n", monsno, formno, sex, shiny);

        auto str = FormatUndergroundStatue(monsno, formno, sex, shiny);

        Logger::log("[UgBoxFormatNames] %s\n", str->asCString().c_str());

        ctx->X[0] = (uint64_t)str;
    }
};

HOOK_DEFINE_INLINE(UgBoxFormatNames_Append) {
    static void Callback(exl::hook::nx64::InlineCtx* ctx) {
        int32_t monsno = (uint16_t)ctx->W[21];
        int32_t formno = (uint16_t)ctx->W[22];
        int32_t sex = (uint8_t)ctx->W[23];
        int32_t shiny = (bool)ctx->W[24];

        system_load_typeinfo(0xa6e0);

        Logger::log("[UgBoxFormatNames] %d, %d, %d, %d\n", monsno, formno, sex, shiny);

        auto str = FormatUndergroundStatue(monsno, formno, sex, shiny);

        Logger::log("[UgBoxFormatNames] %s\n", str->asCString().c_str());

        ctx->X[0] = (uint64_t)str;
    }
};

void exl_ug_forms_main() {
    Dpr_UnderGround_UgPokeLottery_LotteryPoke::InstallAtOffset(0x018bfa90);
    GetUgPokeData::InstallAtOffset(0x01b1b540);

    UgBoxFormatNames_Load::InstallAtOffset(0x019c8fe8);
    UgBoxFormatNames_Append::InstallAtOffset(0x019daafc);
}
